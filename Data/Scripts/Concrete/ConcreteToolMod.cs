using System;
using System.Collections.Generic;
using Digi.ConcreteTool.MP;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum; // HACK allows the use of BlendTypeEnum which is whitelisted but bypasses accessing MyBillboard which is not whitelisted

namespace Digi.ConcreteTool
{
    public enum VoxelActionEnum { ADD_VOXEL, PAINT_VOXEL, REMOVE_VOXEL }

    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class ConcreteToolMod : MySessionComponentBase
    {
        public static ConcreteToolMod Instance = null;

        private bool init = false;
        private bool isThisDedicated = false;

        public readonly Networking Network = new Networking(63311);
        public readonly ChatCommands ChatCommands = new ChatCommands();

        public IMyAutomaticRifleGun holdingTool = null;
        public bool SeenHelp = false;

        private IMyHudNotification toolStatus = null;
        private IMyHudNotification alignStatus = null;
        private IMyHudNotification snapStatus = null;
        private IMyVoxelBase selectedVoxelMap = null;
        private int selectedVoxelMapIndex = 0;
        private long prevVoxelMapId = 0;
        private int selectedVoxelMapTicks = 0;
        private int highlightEntsTicks = 0;

        // TODO more shapes? (var)
        //private PlaceShape selectedShape = PlaceShape.BOX;
        private byte snap = 1;
        private bool snapLock = false;
        private long altitudeLock = 0;
        private byte snapAxis = 0;
        private Vector3D snapVec = Vector3D.NegativeInfinity;
        private bool aligned = false;
        private bool lockAlign = false;
        private float placeDistance = 4f;
        private float placeScale = 1f;
        private MatrixD placeMatrix = MatrixD.Identity;
        private int holdPress = 0;
        private int cooldown = 0;
        private bool soundPlayed_Unable = false;
        private Vector3D target; // for use in delegate to avoid allocations

        private MyVoxelMaterialDefinition material = null;
        private readonly List<MyEntity> highlightEnts = new List<MyEntity>();
        private readonly List<IMyVoxelBase> maps = new List<IMyVoxelBase>();

        //private enum PlaceShape { BOX, SPHERE, CAPSULE, RAMP, }

        private const int HIGHLIGHT_VOXELMAP_MAXTICKS = 120;
        private const int HIGHLIGHT_ENTS_MAXTICKS = 60;

        private const float SCALE_STEP = 0.25f;
        private const float MIN_SCALE = 0.5f;
        private const float MAX_SCALE = 3f;

        private const float DISTANCE_STEP = 0.5f;
        private const float MIN_DISTANCE = 2f;
        private const float MAX_DISTANCE = 6f;

        public const float TOOL_ACTION_MAX_DIST_SQ = 200 * 200; // distance at which player must be from origin to get the sound+particle packet

        public const string CONCRETE_MATERIAL = "Concrete";
        public const string CONCRETE_TOOL = "ConcreteTool";
        public const string CONCRETE_WEAPON_ID = "WeaponConcreteTool";
        public const string CONCRETE_AMMO_ID = "ConcreteMix";
        public const string CONCRETE_GHOST_ID = "ConcreteToolGhost";
        private readonly MyObjectBuilder_AmmoMagazine CONCRETE_MAG = new MyObjectBuilder_AmmoMagazine() { SubtypeName = CONCRETE_AMMO_ID, ProjectilesCount = 1 };
        private readonly MyDefinitionId CONCRETE_MAG_DEFID = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), CONCRETE_AMMO_ID);

        private static readonly MyStringId MATERIAL_SQUARE = MyStringId.GetOrCompute("ConcreteTool_Square");
        private static readonly MyStringId MATERIAL_FADEOUTLINE = MyStringId.GetOrCompute("ConcreteTool_FadeOutLine");
        private static readonly MyStringId MATERIAL_FADEOUTPLANE = MyStringId.GetOrCompute("ConcreteTool_FadeOutPlane");

        private const BlendTypeEnum BLEND_TYPE = BlendTypeEnum.SDR;

        public const float CONCRETE_PLACE_USE = 1f;
        public const float CONCRETE_PAINT_USE = 0.5f;
        private const int REMOVE_TARGET_TICKS = 20;
        private const int RECENT_ACTION_TICKS = 10;

        public override void LoadData()
        {
            Instance = this;
            Log.ModName = "Concrete Tool";
            Log.AutoClose = false;
        }

        public override void BeforeStart()
        {
            isThisDedicated = (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer);

            if(material == null && !MyDefinitionManager.Static.TryGetVoxelMaterialDefinition(CONCRETE_MATERIAL, out material))
            {
                throw new Exception($"ERROR: Could not get the '{CONCRETE_MATERIAL}' voxel material!");
            }

            Network.Register();
            ChatCommands.Register();

            Utils.PreventConcreteToolVanillaFiring();

            SetUpdateOrder(MyUpdateOrder.AfterSimulation);
            init = true;
        }

        protected override void UnloadData()
        {
            try
            {
                if(init)
                {
                    init = false;

                    ChatCommands.Unregister();
                    Network.Unregister();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Instance = null;
            material = null;
            Log.Close();
        }

        /// <summary>
        /// Executes voxel place/remove action (+inventory remove on voxel place) directly if server or sends a packet to execute it server-side.
        /// </summary>
        public void VoxelAction(VoxelActionEnum type, IMyVoxelBase voxelEnt, IMyVoxelShape shape, float scale, IMyCharacter character)
        {
            if(MyAPIGateway.Multiplayer.IsServer)
            {
                bool add = (type == VoxelActionEnum.ADD_VOXEL);

                if(add || type == VoxelActionEnum.PAINT_VOXEL)
                {
                    var inv = character.GetInventory(0);
                    var use = Utils.GetAmmoUsage(type, scale);

                    if(inv.GetItemAmount(CONCRETE_MAG_DEFID) >= use)
                    {
                        if(add)
                            MyAPIGateway.Session.VoxelMaps.FillInShape(voxelEnt, shape, material.Index);
                        else
                            MyAPIGateway.Session.VoxelMaps.PaintInShape(voxelEnt, shape, material.Index);

                        inv.RemoveItemsOfType(use, CONCRETE_MAG, false);
                    }
                    else
                        Log.Error($"Not enough ammo ({use}) for {type} on {character.DisplayName} ({character.EntityId})");
                }
                else if(type == VoxelActionEnum.REMOVE_VOXEL)
                {
                    MyAPIGateway.Session.VoxelMaps.CutOutShape(voxelEnt, shape);
                }
            }
            else
            {
                var origin = shape.Transform.Translation;
                var orientation = Quaternion.CreateFromRotationMatrix(shape.Transform);
                var packet = new PacketVoxelAction(type, voxelEnt.EntityId, scale, origin, orientation, character.EntityId);
                Network.SendToServer(packet);
            }
        }

        /// <summary>
        /// Executes tool effects for clients to see/hear
        /// </summary>
        public static void ToolAction(VoxelActionEnum type, IMyCharacter character, float scale, Vector3D origin)
        {
            #region Recoil animation
            var wepPos = character.Components.Get<MyCharacterWeaponPositionComponent>();

            wepPos.AddBackkick(3f * (1 / 60f));
            #endregion

            #region Sound
            const float MIN_VOLUME = 0.4f;
            const float MAX_VOLUME = 0.8f;
            float soundVolume = MathHelper.Clamp(((scale / 3f) * MAX_VOLUME), MIN_VOLUME, MAX_VOLUME);

            switch(type)
            {
                case VoxelActionEnum.ADD_VOXEL:
                case VoxelActionEnum.PAINT_VOXEL:
                    Utils.PlaySoundAt(character, "ConcreteTool_PlaceConcrete", soundVolume);
                    break;
                case VoxelActionEnum.REMOVE_VOXEL:
                    Utils.PlaySoundAt(character, "ConcreteTool_RemoveTerrain", soundVolume);
                    break;
            }
            #endregion

            #region Particles
            MyParticleEffect particle;
            string particleName = null;
            var matrix = MatrixD.CreateTranslation(origin);

            switch(type)
            {
                case VoxelActionEnum.ADD_VOXEL:
                case VoxelActionEnum.PAINT_VOXEL:
                    particleName = "ConcreteTool_PlaceConcrete";
                    break;
                case VoxelActionEnum.REMOVE_VOXEL:
                    particleName = "ConcreteTool_RemoveTerrain";
                    break;
            }

            if(particleName != null && MyParticlesManager.TryCreateParticleEffect(particleName, ref matrix, ref origin, uint.MaxValue, out particle))
            {
                particle.Loop = false;
                particle.UserScale = scale / 3f;
            }
            #endregion
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if(isThisDedicated || !init)
                    return;

                if(highlightEnts.Count > 0)
                {
                    if(++highlightEntsTicks >= HIGHLIGHT_ENTS_MAXTICKS)
                    {
                        highlightEnts.Clear();
                        highlightEntsTicks = 0;
                    }
                    else
                    {
                        var color = Color.Red * MathHelper.Lerp(0.75f, 0f, ((float)highlightEntsTicks / (float)HIGHLIGHT_ENTS_MAXTICKS));

                        foreach(var ent in highlightEnts)
                        {
                            var matrix = ent.WorldMatrix;
                            var box = (BoundingBoxD)ent.PositionComp.LocalAABB;
                            MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref box, ref color, MySimpleObjectRasterizer.Wireframe, 1, 0.01f, MATERIAL_SQUARE, MATERIAL_SQUARE, false, blendType: BLEND_TYPE);
                        }
                    }
                }

                if(selectedVoxelMapTicks > 0)
                {
                    if(selectedVoxelMap == null)
                    {
                        selectedVoxelMapTicks = 0;
                    }
                    else
                    {
                        selectedVoxelMapTicks--;
                        var matrix = selectedVoxelMap.WorldMatrix;
                        var box = (BoundingBoxD)selectedVoxelMap.LocalAABB;
                        var color = Color.Green * MathHelper.Lerp(0f, 0.5f, ((float)selectedVoxelMapTicks / (float)HIGHLIGHT_VOXELMAP_MAXTICKS));
                        MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref box, ref color, MySimpleObjectRasterizer.Wireframe, 1, 0.01f, MATERIAL_SQUARE, MATERIAL_SQUARE, false, blendType: BLEND_TYPE);
                    }
                }

                if(holdingTool == null || holdingTool.Closed || holdingTool.MarkedForClose)
                {
                    HolsterTool();
                    return;
                }

                var character = MyAPIGateway.Session.ControlledObject as IMyCharacter;

                if(character != null)
                {
                    HoldingTool(character);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void EquipTool(IMyAutomaticRifleGun gun)
        {
            holdingTool = gun;

            if(!SeenHelp)
                SetToolStatus($"Press {InputHandler.GetAssignedGameControlNames(MyControlsSpace.SECONDARY_TOOL_ACTION)} to see Concrete Tools' advanced controls.", 1500, MyFontEnum.White);
        }

        private void HolsterTool()
        {
            holdingTool = null;
            selectedVoxelMap = null;
            toolStatus?.Hide();
        }

        public void HoldingTool(IMyCharacter character)
        {
            bool inputReadable = InputHandler.IsInputReadable();

            if(inputReadable && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SECONDARY_TOOL_ACTION))
            {
                ChatCommands.ShowHelp();
            }

            // detect and revert aim down sights
            // the 1st person view check is required to avoid some 3rd person loopback, it still reverts zoom initiated from 3rd person tho
            //if(character.IsInFirstPersonView && MathHelper.ToDegrees(MyAPIGateway.Session.Camera.FovWithZoom) < MyAPIGateway.Session.Camera.FieldOfViewAngle)
            var comp = character.Components.Get<MyCharacterWeaponPositionComponent>();
            if(comp.IsInIronSight)
            {
                holdingTool.EndShoot(MyShootActionEnum.SecondaryAction);
                holdingTool.Shoot(MyShootActionEnum.SecondaryAction, Vector3.Forward, null, null);
                holdingTool.EndShoot(MyShootActionEnum.SecondaryAction);
            }

            selectedVoxelMap = null;

            // compute target position
            var view = character.GetHeadMatrix(false, true);
            target = view.Translation + (view.Forward * placeDistance);

            // find all voxelmaps intersecting with the target position
            maps.Clear();
            MyAPIGateway.Session.VoxelMaps.GetInstances(maps, FilterVoxelEntities);

            if(maps.Count == 1)
            {
                selectedVoxelMap = maps[0];
            }
            else if(maps.Count > 1)
            {
                if(inputReadable && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE))
                    selectedVoxelMapIndex++;

                if(selectedVoxelMapIndex >= maps.Count)
                    selectedVoxelMapIndex = 0;

                selectedVoxelMap = maps[selectedVoxelMapIndex];

                MyAPIGateway.Utilities.ShowNotification($"([{InputHandler.GetAssignedGameControlNames(MyControlsSpace.USE)}]) Selected Voxel Map: {selectedVoxelMap.StorageName} ({(selectedVoxelMapIndex + 1)} of { maps.Count})", 16, MyFontEnum.Blue);
            }

            maps.Clear();

            bool trigger = inputReadable && MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.PRIMARY_TOOL_ACTION);
            bool paint = inputReadable && MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.CUBE_COLOR_CHANGE);

            if(selectedVoxelMap == null)
            {
                if(trigger || paint)
                {
                    SetToolStatus("Concrete can only be placed on planets or asteroids!", 1500, MyFontEnum.Red);
                }
            }
            else
            {
                if(selectedVoxelMap.EntityId != prevVoxelMapId)
                {
                    prevVoxelMapId = selectedVoxelMap.EntityId;
                    selectedVoxelMapTicks = HIGHLIGHT_VOXELMAP_MAXTICKS;
                }

                ToolProcess(selectedVoxelMap, target, view, trigger, paint);
            }
        }

        private bool FilterVoxelEntities(IMyVoxelBase voxelEnt)
        {
            if(voxelEnt.StorageName == null)
                return false;

            // ignore ghost asteroids
            // explanation: planets don't have physics linked directly to entity and asteroids do have physics and they're disabled when in ghost placement mode, but not null.
            if(!(voxelEnt is MyPlanet) && (voxelEnt.Physics == null || !voxelEnt.Physics.Enabled))
                return false;

            // NOTE: (field)target must be set before using this method!
            var localTarget = Vector3D.Transform(target, voxelEnt.WorldMatrixInvScaled);
            return voxelEnt.LocalAABB.Contains(localTarget) == ContainmentType.Contains;
        }

        private void SetToolStatus(string text, int aliveTime = 300, string font = MyFontEnum.White)
        {
            if(toolStatus == null)
                toolStatus = MyAPIGateway.Utilities.CreateNotification("", aliveTime, font);

            toolStatus.Font = font;
            toolStatus.Text = text;
            toolStatus.AliveTime = aliveTime;
            toolStatus.Show();
        }

        private void SetAlignStatus(string text, int aliveTime = 300, string font = MyFontEnum.White)
        {
            if(alignStatus == null)
                alignStatus = MyAPIGateway.Utilities.CreateNotification("", aliveTime, font);

            alignStatus.Font = font;
            alignStatus.Text = text;
            alignStatus.AliveTime = aliveTime;
            alignStatus.Show();
        }

        private void SetSnapStatus(string text, int aliveTime = 300, string font = MyFontEnum.White)
        {
            if(snapStatus == null)
                snapStatus = MyAPIGateway.Utilities.CreateNotification("", aliveTime, font);

            snapStatus.Font = font;
            snapStatus.Text = text;
            snapStatus.AliveTime = aliveTime;
            snapStatus.Show();
        }

        private bool ToolProcess(IMyVoxelBase voxelEnt, Vector3D target, MatrixD view, bool primaryAction, bool paintAction)
        {
            placeMatrix.Translation = target;

            var planet = voxelEnt as MyPlanet;

            bool inputReadable = InputHandler.IsInputReadable();
            bool invalidPlacement = false;
            bool removeMode = false;
            bool shift = false;
            bool ctrl = false;
            bool alt = false;
            bool snapAxisLock = snapLock && (snap == 0 || snap == 1);

            const string FONTCOLOR_INFO = MyFontEnum.White;
            const string FONTCOLOR_CONSTANT = MyFontEnum.Blue;
            const float GRID_COLOR_ALPHA = 0.6f;

            if(inputReadable)
            {
                shift = MyAPIGateway.Input.IsAnyShiftKeyPressed();
                ctrl = MyAPIGateway.Input.IsAnyCtrlKeyPressed();
                alt = MyAPIGateway.Input.IsAnyAltKeyPressed();
                removeMode = ctrl;

                #region Input: scroll (distance/scale)
                var scroll = MyAPIGateway.Input.DeltaMouseScrollWheelValue();

                if(scroll == 0)
                {
                    if(MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Add) || MyAPIGateway.Input.IsNewKeyPressed(MyKeys.OemPlus)) // numpad + or normal +
                        scroll = 1;
                    else if(MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Subtract) || MyAPIGateway.Input.IsNewKeyPressed(MyKeys.OemMinus)) // numpad - or normal -
                        scroll = -1;
                }

                if(scroll != 0)
                {
                    if(shift)
                    {
                        if(scroll > 0)
                            placeScale += SCALE_STEP;
                        else
                            placeScale -= SCALE_STEP;

                        if(placeScale < MIN_SCALE)
                            placeScale = MIN_SCALE;
                        else if(placeScale > MAX_SCALE)
                            placeScale = MAX_SCALE;
                        else
                            Utils.PlayLocalSound("HudItem");

                        SetToolStatus("Scale: " + Math.Round(placeScale, 2), 1500, FONTCOLOR_INFO);
                    }
                    else if(ctrl)
                    {
                        if(scroll > 0)
                            placeDistance += DISTANCE_STEP;
                        else
                            placeDistance -= DISTANCE_STEP;

                        if(placeDistance < MIN_DISTANCE)
                            placeDistance = MIN_DISTANCE;
                        else if(placeDistance > MAX_DISTANCE)
                            placeDistance = MAX_DISTANCE;
                        else
                            Utils.PlayLocalSound("HudItem");

                        SetToolStatus("Distance: " + Math.Round(placeDistance, 2), 1500, FONTCOLOR_INFO);
                    }
                    // TODO more shapes? (scroll to shape)
                    //else
                    //{
                    //    switch(selectedShape)
                    //    {
                    //        case PlaceShape.BOX:
                    //            selectedShape = (scroll > 0 ? PlaceShape.SPHERE : PlaceShape.RAMP);
                    //            break;
                    //        case PlaceShape.SPHERE:
                    //            selectedShape = (scroll > 0 ? PlaceShape.CAPSULE : PlaceShape.BOX);
                    //            break;
                    //        case PlaceShape.CAPSULE:
                    //            selectedShape = (scroll > 0 ? PlaceShape.RAMP : PlaceShape.SPHERE);
                    //            break;
                    //        case PlaceShape.RAMP:
                    //            selectedShape = (scroll > 0 ? PlaceShape.BOX : PlaceShape.CAPSULE);
                    //            break;
                    //    }
                    //
                    //    SetToolStatus("Shape: " + selectedShape, FONTCOLOR_INFO, 1500);
                    //}
                }
                #endregion Input: scroll (distance/scale)

                #region Input: snapping
                if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.FREE_ROTATION))
                {
                    if(!shift)
                    {
                        snapLock = false;
                        snapAxis = 0;

                        if(++snap > 2)
                            snap = 0;

                        Utils.PlayLocalSound("HudClick");

                        switch(snap)
                        {
                            case 0:
                                SetSnapStatus("Snap: disabled.", 1500, FONTCOLOR_INFO);
                                break;
                            case 1:
                                SetSnapStatus("Snap: voxel-grid.", 1500, FONTCOLOR_INFO);
                                break;
                            case 2:
                                SetSnapStatus("Snap: altitude.", 1500, FONTCOLOR_INFO);
                                break;
                        }
                    }
                    else
                    {
                        Utils.PlayLocalSound("HudItem");

                        if(snap == 0 || snap == 1)
                        {
                            snapLock = true;
                            snapVec = Vector3D.PositiveInfinity;

                            if(++snapAxis > 6)
                                snapAxis = 0;

                            switch(snapAxis)
                            {
                                case 0:
                                    SetSnapStatus("Snap Lock: disabled.", 1500, FONTCOLOR_INFO);
                                    break;
                                case 1:
                                    SetSnapStatus("Snap Lock: X axis", 1500, FONTCOLOR_INFO);
                                    break;
                                case 2:
                                    SetSnapStatus("Snap Lock: Y axis", 1500, FONTCOLOR_INFO);
                                    break;
                                case 3:
                                    SetSnapStatus("Snap Lock: Z axis", 1500, FONTCOLOR_INFO);
                                    break;
                                case 4:
                                    SetSnapStatus("Snap Lock: X/Y plane", 1500, FONTCOLOR_INFO);
                                    break;
                                case 5:
                                    SetSnapStatus("Snap Lock: Y/Z plane", 1500, FONTCOLOR_INFO);
                                    break;
                                case 6:
                                    SetSnapStatus("Snap Lock: Z/X plane", 1500, FONTCOLOR_INFO);
                                    break;
                            }

                            if(snapAxis == 0)
                                snapLock = false;
                        }
                        else if(snap == 2)
                        {
                            snapLock = !snapLock;
                            altitudeLock = int.MinValue;
                        }
                    }
                }
                #endregion Input: snapping

                #region Input: custom alignment
                bool increments = ctrl || shift || alt;
                var rotateInput = Utils.RotateInput(increments);

                if((rotateInput.X != 0 || rotateInput.Y != 0 || rotateInput.Z != 0))
                {
                    lockAlign = false;

                    if(!increments)
                    {
                        var tmpInputs = Utils.RotateInput(true); // checking if this is the first frame player pressed an input axis
                        increments = (tmpInputs.X != 0 || tmpInputs.Y != 0 || tmpInputs.Z != 0); // `increments` var no longer used so we can repurpose it to sound
                    }

                    if(increments)
                        Utils.PlayLocalSound("HudRotateBlock");

                    aligned = true; // next align action will result in a reset
                    double angleRad = ((ctrl ? 90 : (shift ? 15 : (alt ? 1 : 2))) / 180d) * Math.PI;

                    if(snapAxisLock)
                        placeMatrix.Translation = Vector3D.Zero;

                    var cameraMatrix = MyAPIGateway.Session.Camera.WorldMatrix;

                    if(rotateInput.X != 0)
                    {
                        var rotateWorld = Vector3.TransformNormal(new Vector3(-rotateInput.X, 0, 0), cameraMatrix);
                        var dir = placeMatrix.GetClosestDirection(rotateWorld);
                        var axis = placeMatrix.GetDirectionVector(dir);
                        var m = MatrixD.CreateFromAxisAngle(axis, angleRad);
                        m.Translation = Vector3D.Zero;
                        placeMatrix *= m;
                    }

                    if(rotateInput.Y != 0)
                    {
                        var rotateWorld = Vector3.TransformNormal(new Vector3(0, -rotateInput.Y, 0), cameraMatrix);
                        var dir = placeMatrix.GetClosestDirection(rotateWorld);
                        var axis = placeMatrix.GetDirectionVector(dir);
                        var m = MatrixD.CreateFromAxisAngle(axis, angleRad);
                        m.Translation = Vector3D.Zero;
                        placeMatrix *= m;
                    }

                    if(rotateInput.Z != 0)
                    {
                        var rotateWorld = Vector3.TransformNormal(new Vector3(0, 0, rotateInput.Z), cameraMatrix);
                        var dir = placeMatrix.GetClosestDirection(rotateWorld);
                        var axis = placeMatrix.GetDirectionVector(dir);
                        var m = MatrixD.CreateFromAxisAngle(axis, angleRad);
                        m.Translation = Vector3D.Zero;
                        placeMatrix *= m;
                    }

                    placeMatrix = MatrixD.Normalize(placeMatrix);

                    if(snapAxisLock)
                        placeMatrix.Translation = snapVec;
                    else
                        placeMatrix.Translation = target;

                    Vector3D angles;
                    MatrixD.GetEulerAnglesXYZ(ref placeMatrix, out angles);

                    SetAlignStatus($"Align: Custom - {Math.Round(MathHelperD.ToDegrees(angles.X))}° / {Math.Round(MathHelperD.ToDegrees(angles.Y))}° / {Math.Round(MathHelperD.ToDegrees(angles.Z))}°", 500, FONTCOLOR_INFO);
                }
                #endregion Input: custom alignment

                #region Input: alignment
                if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.CUBE_DEFAULT_MOUNTPOINT) || MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE))
                {
                    var grid = MyAPIGateway.CubeBuilder.FindClosestGrid();

                    if(grid != null)
                    {
                        placeMatrix = grid.WorldMatrix;
                        aligned = true; // next align action will result in a reset
                        lockAlign = false;

                        SetAlignStatus("Align: [Aimed ship]", 1500, FONTCOLOR_INFO);

                        if(shift)
                            MyAPIGateway.Utilities.ShowNotification($"NOTE: Shift+{InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_DEFAULT_MOUNTPOINT, true)} when aiming at a ship doesn't lock alignment to it!", 3000, MyFontEnum.Red);

                        Utils.PlayLocalSound("HudItem");
                    }
                    else
                    {
                        if(shift && !lockAlign)
                        {
                            aligned = false; // next align action will result in an align
                            lockAlign = true;
                            Utils.PlayLocalSound("HudClick");
                            // nothing else to do here, it'll be done every tick, below.
                        }
                        else
                        {
                            Utils.PlayLocalSound("HudItem");
                            lockAlign = false;

                            if(aligned)
                            {
                                aligned = false;

                                placeMatrix = MatrixD.Identity;

                                SetAlignStatus("Align: Reset (world axis)", 1500, FONTCOLOR_INFO);
                            }
                            else
                            {
                                aligned = true; // next align action will result in a reset

                                AimToCenter(voxelEnt, view.Forward);

                                SetAlignStatus((planet != null ? "Align: Center of planet" : "Align: Center of asteroid"), 1500, FONTCOLOR_INFO);
                            }
                        }
                    }
                }
                #endregion Input: alignment
            }

            if(lockAlign)
            {
                AimToCenter(voxelEnt, view.Forward);

                SetAlignStatus((planet != null ? "Align Lock: towards center of planet" : "Align Lock: towards center of asteroid"), 16, FONTCOLOR_CONSTANT);
            }

            if(snap == 1) // snap to voxel grid
            {
                // required before snap axis lock but its draw is required after
                placeMatrix.Translation = voxelEnt.PositionLeftBottomCorner + Vector3I.Round(placeMatrix.Translation - voxelEnt.PositionLeftBottomCorner);
            }

            if(snapAxisLock) // snapLock AND (no snap OR snap to voxel grid)
            {
                if(!snapVec.IsValid())
                    snapVec = placeMatrix.Translation;

                const float LINE_WIDTH = 0.01f;
                float lineLength = 10f;
                float lineLengthHalf = lineLength / 2;
                float planeSize = 10f;

                switch(snapAxis)
                {
                    case 1: // X
                        placeMatrix.Translation = snapVec = snapVec - (placeMatrix.Left * placeMatrix.Left.Dot(snapVec)) + (placeMatrix.Left * placeMatrix.Left.Dot(placeMatrix.Translation));
                        break;
                    case 2: // Y
                        placeMatrix.Translation = snapVec = snapVec - (placeMatrix.Up * placeMatrix.Up.Dot(snapVec)) + (placeMatrix.Up * placeMatrix.Up.Dot(placeMatrix.Translation));
                        break;
                    case 3: // Z
                        placeMatrix.Translation = snapVec = snapVec - (placeMatrix.Forward * placeMatrix.Forward.Dot(snapVec)) + (placeMatrix.Forward * placeMatrix.Forward.Dot(placeMatrix.Translation));
                        break;
                    case 4: // X/Y
                        placeMatrix.Translation = snapVec = snapVec - (placeMatrix.Left * placeMatrix.Left.Dot(snapVec)) + (placeMatrix.Left * placeMatrix.Left.Dot(placeMatrix.Translation)) - (placeMatrix.Up * placeMatrix.Up.Dot(snapVec)) + (placeMatrix.Up * placeMatrix.Up.Dot(placeMatrix.Translation));
                        break;
                    case 5: // Y/Z
                        placeMatrix.Translation = snapVec = snapVec - (placeMatrix.Up * placeMatrix.Up.Dot(snapVec)) + (placeMatrix.Up * placeMatrix.Up.Dot(placeMatrix.Translation)) - (placeMatrix.Forward * placeMatrix.Forward.Dot(snapVec)) + (placeMatrix.Forward * placeMatrix.Forward.Dot(placeMatrix.Translation));
                        break;
                    case 6: // Z/X
                        placeMatrix.Translation = snapVec = snapVec - (placeMatrix.Forward * placeMatrix.Forward.Dot(snapVec)) + (placeMatrix.Forward * placeMatrix.Forward.Dot(placeMatrix.Translation)) - (placeMatrix.Left * placeMatrix.Left.Dot(snapVec)) + (placeMatrix.Left * placeMatrix.Left.Dot(placeMatrix.Translation));
                        break;
                }

                if(Vector3D.DistanceSquared(target, placeMatrix.Translation) > 3 * 3)
                {
                    if(!soundPlayed_Unable)
                    {
                        Utils.PlayLocalSound("HudUnable");
                        soundPlayed_Unable = true;
                    }

                    invalidPlacement = true;

                    SetSnapStatus("Snap Lock: Aim closer!", 100, MyFontEnum.Red);
                }

                var color = (invalidPlacement ? Color.Red : Color.Blue) * (snapAxis > 3 ? 0.3f : 0.8f);

                switch(snapAxis)
                {
                    case 1: // X
                        MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, color, placeMatrix.Translation + placeMatrix.Left * lineLengthHalf, placeMatrix.Right, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
                        break;
                    case 2: // Y
                        MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, color, placeMatrix.Translation + placeMatrix.Up * lineLengthHalf, placeMatrix.Down, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
                        break;
                    case 3: // Z
                        MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, color, placeMatrix.Translation + placeMatrix.Forward * lineLengthHalf, placeMatrix.Backward, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
                        break;
                    case 4: // X/Y
                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_FADEOUTPLANE, color, placeMatrix.Translation, placeMatrix.Left, placeMatrix.Up, planeSize, blendType: BLEND_TYPE);
                        break;
                    case 5: // Y/Z
                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_FADEOUTPLANE, color, placeMatrix.Translation, placeMatrix.Up, placeMatrix.Forward, planeSize, blendType: BLEND_TYPE);
                        break;
                    case 6: // Z/X
                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_FADEOUTPLANE, color, placeMatrix.Translation, placeMatrix.Forward, placeMatrix.Left, planeSize, blendType: BLEND_TYPE);
                        break;
                }
            }

            if(snap == 1) // snap to voxel grid
            {
                var gridColor = Color.Wheat * GRID_COLOR_ALPHA;
                const float LINE_WIDTH = 0.0125f;
                const float LINE_LENGTH = 4f;
                const float LINE_LENGTH_HALF = LINE_LENGTH / 2;

                var upHalf = (Vector3D.Up / 2);
                var rightHalf = (Vector3D.Right / 2);
                var forwardHalf = (Vector3D.Forward / 2);

                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + upHalf + -rightHalf + Vector3D.Forward * LINE_LENGTH_HALF, Vector3.Backward, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + upHalf + rightHalf + Vector3D.Forward * LINE_LENGTH_HALF, Vector3.Backward, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -upHalf + -rightHalf + Vector3D.Forward * LINE_LENGTH_HALF, Vector3.Backward, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -upHalf + rightHalf + Vector3D.Forward * LINE_LENGTH_HALF, Vector3.Backward, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);

                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + forwardHalf + -rightHalf + Vector3D.Up * LINE_LENGTH_HALF, Vector3.Down, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + forwardHalf + rightHalf + Vector3D.Up * LINE_LENGTH_HALF, Vector3.Down, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -forwardHalf + -rightHalf + Vector3D.Up * LINE_LENGTH_HALF, Vector3.Down, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -forwardHalf + rightHalf + Vector3D.Up * LINE_LENGTH_HALF, Vector3.Down, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);

                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + forwardHalf + -upHalf + Vector3D.Right * LINE_LENGTH_HALF, Vector3.Left, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + forwardHalf + upHalf + Vector3D.Right * LINE_LENGTH_HALF, Vector3.Left, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -forwardHalf + -upHalf + Vector3D.Right * LINE_LENGTH_HALF, Vector3.Left, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -forwardHalf + upHalf + Vector3D.Right * LINE_LENGTH_HALF, Vector3.Left, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);
            }
            else if(snap == 2) // snap to distance increments from center
            {
                var center = voxelEnt.WorldAABB.Center;
                var dir = (placeMatrix.Translation - center);
                int altitude = (int)Math.Round(dir.Normalize(), 0);
                placeMatrix.Translation = center + (dir * altitude);

                if(snapLock)
                {
                    if(altitudeLock == int.MinValue)
                        altitudeLock = altitude;

                    if(Math.Abs(altitude - altitudeLock) > 3)
                    {
                        if(!soundPlayed_Unable)
                        {
                            Utils.PlayLocalSound("HudUnable");
                            soundPlayed_Unable = true;
                        }

                        invalidPlacement = true;
                    }

                    placeMatrix.Translation = center + (dir * altitudeLock);

                    if(invalidPlacement)
                        SetSnapStatus($"Snap Lock: Altitude at {altitudeLock.ToString("###,###,###,###,###,##0")}m - Aim closer!", 100, MyFontEnum.Red);
                    else
                        SetSnapStatus($"Snap Lock: Altitude at {altitudeLock.ToString("###,###,###,###,###,##0")}m", 100, FONTCOLOR_CONSTANT);
                }

                var gridColor = (invalidPlacement ? Color.Red : Color.Wheat) * GRID_COLOR_ALPHA;
                const float LINE_WIDTH = 0.01f;
                const float LINE_LENGTH = 3f;
                const float LINE_LENGTH_HALF = LINE_LENGTH / 2;
                const float HEIGHT = 12.5f;
                const float HEIGHT_HALF = HEIGHT / 2;
                const float HEIGHT_LINES = 2.5f;

                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + (dir * HEIGHT_HALF), -dir, HEIGHT, LINE_WIDTH, blendType: BLEND_TYPE);

                var vertical = Vector3D.Cross(dir, view.Forward);

                float alpha = -0.6f;

                for(float h = -HEIGHT_LINES; h <= HEIGHT_LINES; h += 1)
                {
                    var color = (invalidPlacement ? Color.Red : Color.Wheat) * (1f - Math.Abs(alpha));

                    MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, color, placeMatrix.Translation - (dir * h) + vertical * LINE_LENGTH_HALF, -vertical, LINE_LENGTH, LINE_WIDTH, blendType: BLEND_TYPE);

                    alpha += 0.2f;
                }

                if(!snapLock)
                    SetSnapStatus($"Snap: Altitude (current: {altitude.ToString("###,###,###,###,###,##0")}m)", 100, FONTCOLOR_CONSTANT);
            }

            int cooldownTicks = Math.Max((int)(15 * placeScale), 15);

            var shape = Session.VoxelMaps.GetBoxVoxelHand();
            var vec = (Vector3D.One / 2) * placeScale;
            var bb = new BoundingBoxD(-vec, vec);
            shape.Boundaries = bb;
            shape.Transform = placeMatrix;

            ToolDrawShape(removeMode, invalidPlacement, cooldownTicks);

            if(invalidPlacement)
                return false;

            soundPlayed_Unable = false;

            if(cooldown > 0 && --cooldown > 0)
                return false;

            #region Ammo check
            if((primaryAction && !removeMode) || paintAction)
            {
                var character = MyAPIGateway.Session.Player.Character;
                var inv = character.GetInventory();
                var type = (paintAction ? VoxelActionEnum.PAINT_VOXEL : (removeMode ? VoxelActionEnum.REMOVE_VOXEL : VoxelActionEnum.ADD_VOXEL));

                if(inv.GetItemAmount(CONCRETE_MAG_DEFID) < Utils.GetAmmoUsage(type, placeScale))
                {
                    Utils.PlayLocalSound("HudUnable");
                    SetToolStatus($"You need Concrete Mix to use this tool!", 1500, MyFontEnum.Red);
                    return false;
                }
            }
            #endregion Ammo check

            #region Actions
            if(paintAction)
            {
                cooldown = cooldownTicks;
                VoxelAction(VoxelActionEnum.PAINT_VOXEL, voxelEnt, shape, placeScale, Session.Player.Character);
                ToolAction(VoxelActionEnum.PAINT_VOXEL, Session.Player.Character, placeScale, shape.Transform.Translation);
                return true;
            }
            else if(primaryAction)
            {
                ++holdPress;

                if(removeMode)
                {
                    if(holdPress % 3 == 0)
                        SetToolStatus("Removing " + (int)(((float)holdPress / (float)REMOVE_TARGET_TICKS) * 100f) + "%...", 100, MyFontEnum.Red);

                    if(holdPress >= REMOVE_TARGET_TICKS)
                    {
                        holdPress = 0;
                        cooldown = cooldownTicks;

                        VoxelAction(VoxelActionEnum.REMOVE_VOXEL, voxelEnt, shape, placeScale, Session.Player.Character);
                        ToolAction(VoxelActionEnum.REMOVE_VOXEL, Session.Player.Character, placeScale, shape.Transform.Translation);
                    }
                }
                else
                {
                    #region Check for obstructions
                    // HACK: This is how the game checks it and I can't prevent it; I must also do it myself to prevent wasting ammo.
                    highlightEnts.Clear();
                    var shapeBB = shape.GetWorldBoundary();
                    MyGamePruningStructure.GetTopMostEntitiesInBox(ref shapeBB, highlightEnts, MyEntityQueryType.Dynamic);
                    highlightEnts.RemoveAll(e => !(e is IMyCubeGrid || e is IMyCharacter || e is IMyFloatingObject) || !e.PositionComp.WorldAABB.Intersects(shapeBB));
                    var localPlayerFound = highlightEnts.Remove((MyEntity)MyAPIGateway.Session.Player.Character);

                    if(localPlayerFound || highlightEnts.Count > 0)
                    {
                        Utils.PlayLocalSound("HudUnable");

                        if(highlightEnts.Count == 0)
                        {
                            SetToolStatus("You're in the way!", 1500, MyFontEnum.Red);
                        }
                        else
                        {
                            SetToolStatus((localPlayerFound ? "You and " : "") + (highlightEnts.Count == 1 ? (localPlayerFound ? "one" : "One") + " thing " : highlightEnts.Count + " things ") + (localPlayerFound || highlightEnts.Count > 1 ? "are" : "is") + " in the way!", 1500, MyFontEnum.Red);
                        }

                        highlightEntsTicks = 0; // reset fadeout timer
                        cooldown = (cooldownTicks - RECENT_ACTION_TICKS); // prevent quick retry, color the box and also prevent highlight by removing their ticks
                        holdPress = 0;
                        return false;
                    }

                    // don't clear highlightEnts because it's used to highlight them elsewhere
                    #endregion

                    cooldown = cooldownTicks;

                    VoxelAction(VoxelActionEnum.ADD_VOXEL, voxelEnt, shape, placeScale, Session.Player.Character);
                    ToolAction(VoxelActionEnum.ADD_VOXEL, Session.Player.Character, placeScale, shape.Transform.Translation);
                    return true;
                }
            }
            else
            {
                holdPress = 0;
            }

            return false;
            #endregion
        }

        private void ToolDrawShape(bool removeMode, bool invalidPlacement, int cooldownTicks)
        {
            var colorWire = Color.Lime * 0.4f;
            var colorFace = Color.Green * 0.2f;

            if(invalidPlacement)
            {
                colorWire = Color.White * 0.1f;
                colorFace = Color.DarkGray * 0.1f;
            }
            else if(cooldown > (cooldownTicks - RECENT_ACTION_TICKS)) // just placed/removed
            {
                colorWire = (removeMode ? Color.Red : Color.Lime) * 2;
                colorFace = (removeMode ? Color.DarkRed : Color.Green) * 0.3f;
            }
            else if(cooldown > 0) // cooldown period
            {
                colorWire = Color.White * 0.1f;
                colorFace = Color.DarkGray * 0.1f;
            }
            else if(removeMode)
            {
                colorWire = Color.Red * 0.4f;
                colorFace = Color.DarkRed * 0.2f;
            }

            // optimized box draw compared to MySimpleObjectDraw.DrawTransparentBox; also allows consistent edge thickness
            MyQuadD quad;
            Vector3D p;
            MatrixD m;
            var halfScale = (placeScale / 2);
            float lineLength = placeScale;
            var material = MATERIAL_SQUARE;
            const float ROTATE_90_RAD = (90f / 180f * MathHelper.Pi); // 90deg in radians
            const float LINE_WIDTH = 0.015f;

            p = placeMatrix.Translation + placeMatrix.Forward * halfScale;
            if(Utils.IsFaceVisible(p, placeMatrix.Forward))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref placeMatrix);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: BLEND_TYPE);
            }

            p = placeMatrix.Translation + placeMatrix.Backward * halfScale;
            if(Utils.IsFaceVisible(p, placeMatrix.Backward))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref placeMatrix);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: BLEND_TYPE);
            }

            p = placeMatrix.Translation + placeMatrix.Left * halfScale;
            m = placeMatrix * MatrixD.CreateFromAxisAngle(placeMatrix.Up, ROTATE_90_RAD);
            if(Utils.IsFaceVisible(p, placeMatrix.Left))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: BLEND_TYPE);
            }

            p = placeMatrix.Translation + placeMatrix.Right * halfScale;
            if(Utils.IsFaceVisible(p, placeMatrix.Right))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: BLEND_TYPE);
            }

            m = placeMatrix * MatrixD.CreateFromAxisAngle(placeMatrix.Left, ROTATE_90_RAD);
            p = placeMatrix.Translation + placeMatrix.Up * halfScale;
            if(Utils.IsFaceVisible(p, placeMatrix.Up))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: BLEND_TYPE);
            }

            p = placeMatrix.Translation + placeMatrix.Down * halfScale;
            if(Utils.IsFaceVisible(p, placeMatrix.Down))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: BLEND_TYPE);
            }

            var upHalf = (placeMatrix.Up * halfScale);
            var rightHalf = (placeMatrix.Right * halfScale);
            var forwardHalf = (placeMatrix.Forward * halfScale);

            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + upHalf + -rightHalf + placeMatrix.Forward * halfScale, placeMatrix.Backward, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + upHalf + rightHalf + placeMatrix.Forward * halfScale, placeMatrix.Backward, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -upHalf + -rightHalf + placeMatrix.Forward * halfScale, placeMatrix.Backward, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -upHalf + rightHalf + placeMatrix.Forward * halfScale, placeMatrix.Backward, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);

            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + forwardHalf + -rightHalf + placeMatrix.Up * halfScale, placeMatrix.Down, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + forwardHalf + rightHalf + placeMatrix.Up * halfScale, placeMatrix.Down, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -forwardHalf + -rightHalf + placeMatrix.Up * halfScale, placeMatrix.Down, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -forwardHalf + rightHalf + placeMatrix.Up * halfScale, placeMatrix.Down, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);

            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + forwardHalf + -upHalf + placeMatrix.Right * halfScale, placeMatrix.Left, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + forwardHalf + upHalf + placeMatrix.Right * halfScale, placeMatrix.Left, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -forwardHalf + -upHalf + placeMatrix.Right * halfScale, placeMatrix.Left, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -forwardHalf + upHalf + placeMatrix.Right * halfScale, placeMatrix.Left, lineLength, LINE_WIDTH, blendType: BLEND_TYPE);

            // TODO more shapes? (set shape)
            //switch(selectedShape)
            //{
            //    default: // BOX
            //        {
            //            // box code here
            //            break;
            //        }
            //    case PlaceShape.SPHERE:
            //        {
            //            var shape = MyAPIGateway.Session.VoxelMaps.GetSphereVoxelHand();
            //            shape.Center = placeMatrix.Translation;
            //            shape.Radius = placeScale;
            //            placeShape = shape;
            //
            //            MySimpleObjectDraw.DrawTransparentSphere(ref placeMatrix, shape.Radius, ref colorWire, MySimpleObjectRasterizer.Wireframe, 12, MATERIAL_SQUARE, MATERIAL_SQUARE, 0.01f, blendType: BLEND_TYPE);
            //            MySimpleObjectDraw.DrawTransparentSphere(ref placeMatrix, shape.Radius, ref colorFace, MySimpleObjectRasterizer.Solid, 12, MATERIAL_SQUARE, MATERIAL_SQUARE, 0.01f, blendType: BLEND_TYPE);
            //            break;
            //        }
            //    case PlaceShape.CAPSULE:
            //        {
            //            var shape = MyAPIGateway.Session.VoxelMaps.GetCapsuleVoxelHand();
            //            shape.Radius = placeScale;
            //            // height
            //            placeShape = shape;
            //
            //            MySimpleObjectDraw.DrawTransparentCapsule(ref placeMatrix, shape.Radius, 2, ref colorWire, 12, MATERIAL_SQUARE, blendType: BLEND_TYPE);
            //            break;
            //        }
            //    case PlaceShape.RAMP:
            //        {
            //            var shape = MyAPIGateway.Session.VoxelMaps.GetRampVoxelHand();
            //            shape.RampNormal = Vector3D.Forward; // TODO direction
            //            shape.RampNormalW = placeScale;
            //            var vec = (Vector3D.One / 2) * placeScale;
            //            var box = new BoundingBoxD(-vec, vec);
            //            shape.Boundaries = box;
            //            placeShape = shape;
            //
            //            MySimpleObjectDraw.DrawTransparentRamp(ref placeMatrix, ref box, ref colorWire, MATERIAL_SQUARE, blendType: BLEND_TYPE);
            //            break;
            //        }
            //}
        }

        private void AimToCenter(IMyVoxelBase voxelEnt, Vector3D forward)
        {
            var center = voxelEnt.WorldAABB.Center;
            var dir = (placeMatrix.Translation - center);
            var altitude = dir.Normalize();
            placeMatrix = MatrixD.CreateFromDir(dir, forward);
            placeMatrix.Translation = center + (dir * altitude);
        }
    }
}
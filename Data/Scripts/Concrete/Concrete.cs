using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

using static Sandbox.ModAPI.MyAPIGateway;

namespace Digi.Concrete
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Concrete : MySessionComponentBase
    {
        public override void LoadData()
        {
            Log.SetUp("Concrete Tool", 396679430, "ConcreteTool");
        }

        public static Concrete instance = null;

        private bool init = false;
        private bool isThisDedicated = false;

        public IMyAutomaticRifleGun holdingTool = null;

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
        private bool seenHelp = false;

        private MyVoxelMaterialDefinition material = null;
        private readonly List<MyEntity> highlightEnts = new List<MyEntity>();
        private readonly List<IMyVoxelBase> maps = new List<IMyVoxelBase>();

        //private enum PlaceShape { BOX, SPHERE, CAPSULE, RAMP, }
        private enum PacketType { PLACE_VOXEL, PAINT_VOXEL, REMOVE_VOXEL }

        public const ushort PACKET = 63311;

        private const int HIGHLIGHT_VOXELMAP_MAXTICKS = 120;
        private const int HIGHLIGHT_ENTS_MAXTICKS = 60;

        private const float SCALE_STEP = 0.25f;
        private const float MIN_SCALE = 0.5f;
        private const float MAX_SCALE = 3f;

        private const float DISTANCE_STEP = 0.5f;
        private const float MIN_DISTANCE = 2f;
        private const float MAX_DISTANCE = 6f;

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

        public const float CONCRETE_PLACE_USE_PERMETER = 1f;
        public const float CONCRETE_PAINT_USE_PERMETER = 0.5f;

        public void Init()
        {
            instance = this;
            Log.Init();

            init = true;
            isThisDedicated = (Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer);

            Utilities.MessageEntered += MessageEntered;

            if(MyAPIGateway.Multiplayer.IsServer)
            {
                MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET, ReceivedPacket);
            }

            if(material == null && !MyDefinitionManager.Static.TryGetVoxelMaterialDefinition(CONCRETE_MATERIAL, out material))
            {
                throw new Exception("ERROR: Could not get the '" + CONCRETE_MATERIAL + "' voxel material!");
            }

            // make the concrete tool not be able to shoot normally, to avoid needing to add ammo and the stupid hardcoded screen shake
            var gunDef = MyDefinitionManager.Static.GetWeaponDefinition(new MyDefinitionId(typeof(MyObjectBuilder_WeaponDefinition), CONCRETE_WEAPON_ID));

            for(int i = 0; i < gunDef.WeaponAmmoDatas.Length; i++)
            {
                var ammoData = gunDef.WeaponAmmoDatas[i];

                if(ammoData == null)
                    continue;

                ammoData.ShootIntervalInMiliseconds = int.MaxValue;
            }
        }

        protected override void UnloadData()
        {
            instance = null;
            material = null;

            try
            {
                if(init)
                {
                    init = false;

                    Utilities.MessageEntered -= MessageEntered;

                    if(MyAPIGateway.Multiplayer.IsServer)
                    {
                        MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET, ReceivedPacket);
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Log.Close();
        }

        private void ReceivedPacket(byte[] bytes)
        {
            try
            {
                if(!MyAPIGateway.Session.IsServer) // ensure this only gets called server side even though it's only registered server side
                    return;

                int index = 0;

                var type = (PacketType)bytes[index];
                index += sizeof(byte);

                long voxelsId = BitConverter.ToInt64(bytes, index);
                index += sizeof(long);

                var voxels = MyAPIGateway.Entities.GetEntityById(voxelsId) as IMyVoxelMap;

                if(voxels == null)
                    return;

                float scale = BitConverter.ToSingle(bytes, index);
                index += sizeof(float);

                Vector3D origin = new Vector3D(BitConverter.ToDouble(bytes, index),
                                          BitConverter.ToDouble(bytes, index + sizeof(double)),
                                          BitConverter.ToDouble(bytes, index + sizeof(double) * 2));
                index += sizeof(double) * 3;

                Vector3 forward = new Vector3(BitConverter.ToSingle(bytes, index),
                                          BitConverter.ToSingle(bytes, index + sizeof(float)),
                                          BitConverter.ToSingle(bytes, index + sizeof(float) * 2));
                index += sizeof(float) * 3;

                Vector3 up = new Vector3(BitConverter.ToSingle(bytes, index),
                                     BitConverter.ToSingle(bytes, index + sizeof(float)),
                                     BitConverter.ToSingle(bytes, index + sizeof(float) * 2));
                index += sizeof(float) * 3;

                var shape = MyAPIGateway.Session.VoxelMaps.GetBoxVoxelHand();
                var vec = (Vector3D.One / 2) * scale;
                shape.Boundaries = new BoundingBoxD(-vec, vec);
                shape.Transform = MatrixD.CreateWorld(origin, forward, up);

                if(type == PacketType.PLACE_VOXEL || type == PacketType.PAINT_VOXEL)
                {
                    byte materialIndex = bytes[index];
                    index += sizeof(byte);

                    long charId = BitConverter.ToInt64(bytes, index);
                    index += sizeof(long);

                    var character = MyAPIGateway.Entities.GetEntityById(charId) as IMyCharacter;

                    if(character == null)
                        return;

                    VoxelAction(type, voxels, shape, scale, materialIndex, character);
                }
                else
                {
                    VoxelAction(type, voxels, shape, scale);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        /// <summary>
        /// Executes voxel place/remove action (+inventory remove on voxel place) by sending a packet from clients or executing it directly if already server.
        /// </summary>
        private void VoxelAction(PacketType type, IMyVoxelBase voxels, IMyVoxelShape shape, float scale, byte materialIndex = 0, IMyCharacter character = null)
        {
            if(MyAPIGateway.Session.IsServer)
            {
                switch(type)
                {
                    case PacketType.PLACE_VOXEL:
                        {
                            MyAPIGateway.Session.VoxelMaps.FillInShape(voxels, shape, materialIndex);
                            var inv = character.GetInventory(0) as IMyInventory;
                            inv.RemoveItemsOfType((MyFixedPoint)(CONCRETE_PLACE_USE_PERMETER * scale), CONCRETE_MAG, false); // scale ammo usage with placement size
                            break;
                        }
                    case PacketType.PAINT_VOXEL:
                        {
                            MyAPIGateway.Session.VoxelMaps.PaintInShape(voxels, shape, materialIndex);
                            var inv = character.GetInventory(0) as IMyInventory;
                            inv.RemoveItemsOfType((MyFixedPoint)(CONCRETE_PAINT_USE_PERMETER * scale), CONCRETE_MAG, false); // scale ammo usage with placement size
                            break;
                        }
                    case PacketType.REMOVE_VOXEL:
                        {
                            MyAPIGateway.Session.VoxelMaps.CutOutShape(voxels, shape);
                            break;
                        }
                }
            }
            else
            {
                bool extraArgs = (type == PacketType.PLACE_VOXEL || type == PacketType.PAINT_VOXEL);

                if(extraArgs && character == null)
                    throw new Exception("Character is null!");

                int len = sizeof(byte) + sizeof(long) + sizeof(float) + (sizeof(double) * 3) + (sizeof(float) * 3) + (sizeof(float) * 3);

                if(extraArgs)
                    len += sizeof(long) + sizeof(byte);

                var bytes = new byte[len];
                bytes[0] = (byte)type;
                len = sizeof(byte);

                PacketHandler.AddToArray(voxels.EntityId, ref len, ref bytes);
                PacketHandler.AddToArray(scale, ref len, ref bytes);
                PacketHandler.AddToArray(shape.Transform.Translation, ref len, ref bytes);
                PacketHandler.AddToArray((Vector3)shape.Transform.Forward, ref len, ref bytes);
                PacketHandler.AddToArray((Vector3)shape.Transform.Up, ref len, ref bytes);

                if(extraArgs)
                {
                    bytes[len] = materialIndex;
                    len += sizeof(byte);

                    PacketHandler.AddToArray(character.EntityId, ref len, ref bytes);
                }

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if(!init)
                {
                    if(MyAPIGateway.Session == null)
                        return;

                    Init();
                }

                if(isThisDedicated)
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
                            MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref box, ref color, MySimpleObjectRasterizer.Wireframe, 1, 0.01f, MATERIAL_SQUARE, MATERIAL_SQUARE, false);
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
                        MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref box, ref color, MySimpleObjectRasterizer.Wireframe, 1, 0.01f, MATERIAL_SQUARE, MATERIAL_SQUARE, false);
                    }
                }

                if(holdingTool == null || holdingTool.Closed || holdingTool.MarkedForClose)
                {
                    HolsterTool();
                    return;
                }

                var character = MyAPIGateway.Session.ControlledObject as IMyCharacter;

                if(character == null)
                    return;

                HoldingTool(character);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public static void PlaySound(string name)
        {
            var emitter = new MyEntity3DSoundEmitter((MyEntity)MyAPIGateway.Session.ControlledObject.Entity);
            emitter.PlaySingleSound(new MySoundPair(name));
        }

        public void DrawTool(IMyAutomaticRifleGun gun)
        {
            holdingTool = gun;

            if(!seenHelp)
                SetToolStatus($"Press {InputHandler.GetAssignedGameControlNames(MyControlsSpace.SECONDARY_TOOL_ACTION)} to see Concrete Tools' advanced controls.", 1500, MyFontEnum.White);
        }

        public void HolsterTool()
        {
            holdingTool = null;
            selectedVoxelMap = null;
            toolStatus?.Hide();
        }

        public void HoldingTool(IMyCharacter character)
        {
            bool inputReadable = InputHandler.IsInputReadable();

            if(inputReadable && Input.IsNewGameControlPressed(MyControlsSpace.SECONDARY_TOOL_ACTION))
            {
                ShowHelp();
            }

            // detect and revert aim down sights
            // the 1st person view check is required to avoid some 3rd person loopback, it still reverts zoom initiated from 3rd person tho
            if(character.IsInFirstPersonView && MathHelper.ToDegrees(MyAPIGateway.Session.Camera.FovWithZoom) < MyAPIGateway.Session.Camera.FieldOfViewAngle)
            {
                holdingTool.EndShoot(MyShootActionEnum.SecondaryAction);
                holdingTool.Shoot(MyShootActionEnum.SecondaryAction, Vector3.Forward, null, null);
                holdingTool.EndShoot(MyShootActionEnum.SecondaryAction);
            }

            // compute target position
            var view = character.GetHeadMatrix(false, true);
            var target = view.Translation + (view.Forward * placeDistance);
            selectedVoxelMap = null;
            maps.Clear();

            // find all voxelmaps intersecting with the target position
            MyAPIGateway.Session.VoxelMaps.GetInstances(maps, delegate (IMyVoxelBase map)
            {
                if(map.StorageName == null)
                    return false;

                // ignore ghost asteroids
                // explanation: planets don't have physics linked directly to entity and asteroids do have physics and they're disabled when in ghost placement mode, but not null.
                if(!(map is MyPlanet) && (map.Physics == null || !map.Physics.Enabled))
                    return false;

                var localTarget = Vector3D.Transform(target, map.WorldMatrixInvScaled);
                return map.LocalAABB.Contains(localTarget) == ContainmentType.Contains;
            });

            if(maps.Count == 1)
            {
                selectedVoxelMap = maps[0];
            }
            else if(maps.Count > 1)
            {
                if(inputReadable && Input.IsNewGameControlPressed(MyControlsSpace.USE))
                    selectedVoxelMapIndex++;

                if(selectedVoxelMapIndex >= maps.Count)
                    selectedVoxelMapIndex = 0;

                selectedVoxelMap = maps[selectedVoxelMapIndex];
                Utilities.ShowNotification("[" + InputHandler.GetAssignedGameControlNames(MyControlsSpace.USE) + "] Selected Voxel Map: " + selectedVoxelMap.StorageName + " (" + (selectedVoxelMapIndex + 1) + " of " + maps.Count + ")", 16, MyFontEnum.Blue);
            }

            maps.Clear();

            bool trigger = inputReadable && Input.IsGameControlPressed(MyControlsSpace.PRIMARY_TOOL_ACTION);
            bool paint = inputReadable && Input.IsGameControlPressed(MyControlsSpace.CUBE_COLOR_CHANGE);

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

        private void SetToolStatus(string text, int aliveTime = 300, string font = MyFontEnum.White)
        {
            if(toolStatus == null)
                toolStatus = Utilities.CreateNotification("", aliveTime, font);

            toolStatus.Font = font;
            toolStatus.Text = text;
            toolStatus.AliveTime = aliveTime;
            toolStatus.Show();
        }

        private void SetAlignStatus(string text, int aliveTime = 300, string font = MyFontEnum.White)
        {
            if(alignStatus == null)
                alignStatus = Utilities.CreateNotification("", aliveTime, font);

            alignStatus.Font = font;
            alignStatus.Text = text;
            alignStatus.AliveTime = aliveTime;
            alignStatus.Show();
        }

        private void SetSnapStatus(string text, int aliveTime = 300, string font = MyFontEnum.White)
        {
            if(snapStatus == null)
                snapStatus = Utilities.CreateNotification("", aliveTime, font);

            snapStatus.Font = font;
            snapStatus.Text = text;
            snapStatus.AliveTime = aliveTime;
            snapStatus.Show();
        }

        private bool ToolProcess(IMyVoxelBase voxels, Vector3D target, MatrixD view, bool trigger, bool paint)
        {
            placeMatrix.Translation = target;

            var planet = voxels as MyPlanet;
            IMyVoxelShape placeShape = null;

            bool inputReadable = InputHandler.IsInputReadable();
            bool removeMode = false;
            bool shift = false;
            bool ctrl = false;
            bool alt = false;
            bool snapAxisLock = snapLock && (snap == 0 || snap == 1);

            const string FONTCOLOR_INFO = MyFontEnum.White;
            const string FONTCOLOR_CONSTANT = MyFontEnum.Blue;

            if(inputReadable)
            {
                shift = Input.IsAnyShiftKeyPressed();
                ctrl = Input.IsAnyCtrlKeyPressed();
                alt = Input.IsAnyAltKeyPressed();
                removeMode = ctrl;

                #region Input: scroll (distance/scale)
                var scroll = Input.DeltaMouseScrollWheelValue();

                if(scroll == 0)
                {
                    if(Input.IsNewKeyPressed(MyKeys.Add) || Input.IsNewKeyPressed(MyKeys.OemPlus)) // numpad + or normal +
                        scroll = 1;
                    else if(Input.IsNewKeyPressed(MyKeys.Subtract) || Input.IsNewKeyPressed(MyKeys.OemMinus)) // numpad - or normal -
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
                            PlaySound("HudItem");

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
                            PlaySound("HudItem");

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
                if(Input.IsNewGameControlPressed(MyControlsSpace.FREE_ROTATION))
                {
                    if(!shift)
                    {
                        snapLock = false;
                        snapAxis = 0;

                        if(++snap > 2)
                            snap = 0;

                        PlaySound("HudClick");

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
                        PlaySound("HudItem");

                        if(snap == 0 || snap == 1)
                        {
                            snapLock = true;
                            snapVec = Vector3D.PositiveInfinity;

                            if(ctrl)
                            {
                                if(snapAxis < 3)
                                    snapAxis = 4;
                                else if(++snapAxis > 6)
                                    snapAxis = 0;
                            }
                            else
                            {
                                if(snapAxis > 3)
                                    snapAxis = 1;
                                else if(++snapAxis > 3)
                                    snapAxis = 0;
                            }

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
                var rotateInput = RotateInput(increments);

                if((rotateInput.X != 0 || rotateInput.Y != 0 || rotateInput.Z != 0))
                {
                    lockAlign = false;

                    if(!increments)
                    {
                        var tmpInputs = RotateInput(true); // checking if this is the first frame player pressed an input axis
                        increments = (tmpInputs.X != 0 || tmpInputs.Y != 0 || tmpInputs.Z != 0); // `increments` var no longer used so we can repurpose it to sound
                    }

                    if(increments)
                        PlaySound("HudRotateBlock");

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

                    SetAlignStatus($"Align: Custom - {Math.Round(MathHelper.ToDegrees(angles.X))}° / {Math.Round(MathHelper.ToDegrees(angles.Y))}° / {Math.Round(MathHelper.ToDegrees(angles.Z))}°", 500, FONTCOLOR_INFO);
                }
                #endregion Input: custom alignment

                #region Input: alignment
                if(Input.IsNewGameControlPressed(MyControlsSpace.CUBE_DEFAULT_MOUNTPOINT) || Input.IsNewGameControlPressed(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE))
                {
                    var grid = CubeBuilder.FindClosestGrid();

                    if(grid != null)
                    {
                        placeMatrix = grid.WorldMatrix;
                        aligned = true; // next align action will result in a reset
                        lockAlign = false;

                        SetAlignStatus("Align: Aimed ship", 1500, FONTCOLOR_INFO);

                        if(shift)
                            Utilities.ShowNotification($"NOTE: Shift+{InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_DEFAULT_MOUNTPOINT, true)} when aiming at a ship doesn't lock alignment to it!", 3000, MyFontEnum.Red);

                        PlaySound("HudItem");
                    }
                    else
                    {
                        if(shift && !lockAlign)
                        {
                            aligned = false; // next align action will result in an align
                            lockAlign = true;
                            PlaySound("HudClick");
                            // nothing else to do here, it'll be done every tick, below.
                        }
                        else
                        {
                            PlaySound("HudItem");
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

                                AimToCenter(voxels, view.Forward);

                                SetAlignStatus((planet != null ? "Align: Center of planet" : "Align: Center of asteroid"), 1500, FONTCOLOR_INFO);
                            }
                        }
                    }
                }
                #endregion Input: alignment
            }

            if(lockAlign)
            {
                AimToCenter(voxels, view.Forward);

                SetAlignStatus((planet != null ? "Align Lock: towards center of planet" : "Align Lock: towards center of asteroid"), 16, FONTCOLOR_CONSTANT);
            }

            const float GRID_COLOR_ALPHA = 0.6f;

            bool invalidPlacement = false;

            if(snap == 1) // snap to voxel grid
            {
                // required before snap axis lock but its draw is required after
                placeMatrix.Translation = voxels.PositionLeftBottomCorner + Vector3I.Round(placeMatrix.Translation - voxels.PositionLeftBottomCorner);
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
                        PlaySound("HudUnable");
                        soundPlayed_Unable = true;
                    }

                    invalidPlacement = true;

                    SetSnapStatus("Snap Lock: Aim closer!", 100, MyFontEnum.Red);
                }

                var color = (invalidPlacement ? Color.Red : Color.Blue) * (snapAxis > 3 ? 0.3f : 0.8f);

                switch(snapAxis)
                {
                    case 1: // X
                        MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, color, placeMatrix.Translation + placeMatrix.Left * lineLengthHalf, placeMatrix.Right, lineLength, LINE_WIDTH);
                        break;
                    case 2: // Y
                        MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, color, placeMatrix.Translation + placeMatrix.Up * lineLengthHalf, placeMatrix.Down, lineLength, LINE_WIDTH);
                        break;
                    case 3: // Z
                        MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, color, placeMatrix.Translation + placeMatrix.Forward * lineLengthHalf, placeMatrix.Backward, lineLength, LINE_WIDTH);
                        break;
                    case 4: // X/Y
                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_FADEOUTPLANE, color, placeMatrix.Translation, placeMatrix.Left, placeMatrix.Up, planeSize);
                        break;
                    case 5: // Y/Z
                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_FADEOUTPLANE, color, placeMatrix.Translation, placeMatrix.Up, placeMatrix.Forward, planeSize);
                        break;
                    case 6: // Z/X
                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_FADEOUTPLANE, color, placeMatrix.Translation, placeMatrix.Forward, placeMatrix.Left, planeSize);
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

                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + upHalf + -rightHalf + Vector3D.Forward * LINE_LENGTH_HALF, Vector3.Backward, LINE_LENGTH, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + upHalf + rightHalf + Vector3D.Forward * LINE_LENGTH_HALF, Vector3.Backward, LINE_LENGTH, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -upHalf + -rightHalf + Vector3D.Forward * LINE_LENGTH_HALF, Vector3.Backward, LINE_LENGTH, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -upHalf + rightHalf + Vector3D.Forward * LINE_LENGTH_HALF, Vector3.Backward, LINE_LENGTH, LINE_WIDTH);

                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + forwardHalf + -rightHalf + Vector3D.Up * LINE_LENGTH_HALF, Vector3.Down, LINE_LENGTH, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + forwardHalf + rightHalf + Vector3D.Up * LINE_LENGTH_HALF, Vector3.Down, LINE_LENGTH, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -forwardHalf + -rightHalf + Vector3D.Up * LINE_LENGTH_HALF, Vector3.Down, LINE_LENGTH, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -forwardHalf + rightHalf + Vector3D.Up * LINE_LENGTH_HALF, Vector3.Down, LINE_LENGTH, LINE_WIDTH);

                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + forwardHalf + -upHalf + Vector3D.Right * LINE_LENGTH_HALF, Vector3.Left, LINE_LENGTH, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + forwardHalf + upHalf + Vector3D.Right * LINE_LENGTH_HALF, Vector3.Left, LINE_LENGTH, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -forwardHalf + -upHalf + Vector3D.Right * LINE_LENGTH_HALF, Vector3.Left, LINE_LENGTH, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + -forwardHalf + upHalf + Vector3D.Right * LINE_LENGTH_HALF, Vector3.Left, LINE_LENGTH, LINE_WIDTH);
            }
            else if(snap == 2) // snap to distance increments from center
            {
                var center = voxels.WorldAABB.Center;
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
                            PlaySound("HudUnable");
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

                MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, gridColor, placeMatrix.Translation + (dir * HEIGHT_HALF), -dir, HEIGHT, LINE_WIDTH);

                var vertical = Vector3D.Cross(dir, view.Forward);

                float alpha = -0.6f;

                for(float h = -HEIGHT_LINES; h <= HEIGHT_LINES; h += 1)
                {
                    var color = (invalidPlacement ? Color.Red : Color.Wheat) * (1f - Math.Abs(alpha));

                    MyTransparentGeometry.AddLineBillboard(MATERIAL_FADEOUTLINE, color, placeMatrix.Translation - (dir * h) + vertical * LINE_LENGTH_HALF, -vertical, LINE_LENGTH, LINE_WIDTH);

                    alpha += 0.2f;
                }

                if(!snapLock)
                    SetSnapStatus($"Snap: Altitude (current: {altitude.ToString("###,###,###,###,###,##0")}m)", 100, FONTCOLOR_CONSTANT);
            }

            int cooldownTicks = Math.Max((int)(15 * placeScale), 15);

            const int removeTargetTicks = 20;
            const int recentActionTicks = 10;

            var colorWire = Color.Lime * 0.4f;
            var colorFace = Color.Green * 0.2f;

            if(invalidPlacement)
            {
                colorWire = Color.White * 0.1f;
                colorFace = Color.DarkGray * 0.1f;
            }
            else if(cooldown > (cooldownTicks - recentActionTicks)) // just placed/removed
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

            var shape = Session.VoxelMaps.GetBoxVoxelHand();
            var vec = (Vector3D.One / 2) * placeScale;
            var bb = new BoundingBoxD(-vec, vec);
            shape.Boundaries = bb;
            shape.Transform = placeMatrix;
            placeShape = shape;

            // optimized box draw; also allows consistent edge thickness
            {
                MyQuadD quad;
                Vector3D p;
                MatrixD m;
                var halfScale = (placeScale / 2);
                var rad = MathHelper.ToRadians(90);
                var material = MATERIAL_SQUARE;
                const float LINE_WIDTH = 0.015f;
                float lineLength = placeScale;

                p = placeMatrix.Translation + placeMatrix.Forward * halfScale;
                if(IsFaceVisible(p, placeMatrix.Forward))
                {
                    MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref placeMatrix);
                    MyTransparentGeometry.AddQuad(MATERIAL_SQUARE, ref quad, colorFace, ref p);
                }

                p = placeMatrix.Translation + placeMatrix.Backward * halfScale;
                if(IsFaceVisible(p, placeMatrix.Backward))
                {
                    MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref placeMatrix);
                    MyTransparentGeometry.AddQuad(MATERIAL_SQUARE, ref quad, colorFace, ref p);
                }

                p = placeMatrix.Translation + placeMatrix.Left * halfScale;
                m = placeMatrix * MatrixD.CreateFromAxisAngle(placeMatrix.Up, rad);
                if(IsFaceVisible(p, placeMatrix.Left))
                {
                    MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                    MyTransparentGeometry.AddQuad(MATERIAL_SQUARE, ref quad, colorFace, ref p);
                }

                p = placeMatrix.Translation + placeMatrix.Right * halfScale;
                if(IsFaceVisible(p, placeMatrix.Right))
                {
                    MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                    MyTransparentGeometry.AddQuad(MATERIAL_SQUARE, ref quad, colorFace, ref p);
                }

                m = placeMatrix * MatrixD.CreateFromAxisAngle(placeMatrix.Left, rad);
                p = placeMatrix.Translation + placeMatrix.Up * halfScale;
                if(IsFaceVisible(p, placeMatrix.Up))
                {
                    MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                    MyTransparentGeometry.AddQuad(MATERIAL_SQUARE, ref quad, colorFace, ref p);
                }

                p = placeMatrix.Translation + placeMatrix.Down * halfScale;
                if(IsFaceVisible(p, placeMatrix.Down))
                {
                    MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                    MyTransparentGeometry.AddQuad(MATERIAL_SQUARE, ref quad, colorFace, ref p);
                }

                var upHalf = (placeMatrix.Up * halfScale);
                var rightHalf = (placeMatrix.Right * halfScale);
                var forwardHalf = (placeMatrix.Forward * halfScale);

                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + upHalf + -rightHalf + placeMatrix.Forward * halfScale, placeMatrix.Backward, lineLength, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + upHalf + rightHalf + placeMatrix.Forward * halfScale, placeMatrix.Backward, lineLength, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -upHalf + -rightHalf + placeMatrix.Forward * halfScale, placeMatrix.Backward, lineLength, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -upHalf + rightHalf + placeMatrix.Forward * halfScale, placeMatrix.Backward, lineLength, LINE_WIDTH);

                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + forwardHalf + -rightHalf + placeMatrix.Up * halfScale, placeMatrix.Down, lineLength, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + forwardHalf + rightHalf + placeMatrix.Up * halfScale, placeMatrix.Down, lineLength, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -forwardHalf + -rightHalf + placeMatrix.Up * halfScale, placeMatrix.Down, lineLength, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -forwardHalf + rightHalf + placeMatrix.Up * halfScale, placeMatrix.Down, lineLength, LINE_WIDTH);

                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + forwardHalf + -upHalf + placeMatrix.Right * halfScale, placeMatrix.Left, lineLength, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + forwardHalf + upHalf + placeMatrix.Right * halfScale, placeMatrix.Left, lineLength, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -forwardHalf + -upHalf + placeMatrix.Right * halfScale, placeMatrix.Left, lineLength, LINE_WIDTH);
                MyTransparentGeometry.AddLineBillboard(material, colorWire, placeMatrix.Translation + -forwardHalf + upHalf + placeMatrix.Right * halfScale, placeMatrix.Left, lineLength, LINE_WIDTH);
            }

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
            //            MySimpleObjectDraw.DrawTransparentSphere(ref placeMatrix, shape.Radius, ref colorWire, MySimpleObjectRasterizer.Wireframe, 12, MATERIAL_SQUARE, MATERIAL_SQUARE, 0.01f);
            //            MySimpleObjectDraw.DrawTransparentSphere(ref placeMatrix, shape.Radius, ref colorFace, MySimpleObjectRasterizer.Solid, 12, MATERIAL_SQUARE, MATERIAL_SQUARE, 0.01f);
            //            break;
            //        }
            //    case PlaceShape.CAPSULE:
            //        {
            //            var shape = MyAPIGateway.Session.VoxelMaps.GetCapsuleVoxelHand();
            //            shape.Radius = placeScale;
            //            // height
            //            placeShape = shape;
            //
            //            MySimpleObjectDraw.DrawTransparentCapsule(ref placeMatrix, shape.Radius, 2, ref colorWire, 12, MATERIAL_SQUARE);
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
            //            MySimpleObjectDraw.DrawTransparentRamp(ref placeMatrix, ref box, ref colorWire, MATERIAL_SQUARE);
            //            break;
            //        }
            //}


            if(invalidPlacement)
                return false;

            soundPlayed_Unable = false;

            if(cooldown > 0 && --cooldown > 0)
                return false;

            if(paint)
            {
                VoxelAction(PacketType.PAINT_VOXEL, voxels, placeShape, placeScale, material.Index, Session.Player.Character);
                PlaySound("HudColorBlock");
                cooldown = cooldownTicks;
                return true;
            }
            else if(trigger)
            {
                ++holdPress;

                if(removeMode)
                {
                    if(holdPress % 3 == 0)
                        SetToolStatus("Removing " + (int)(((float)holdPress / (float)removeTargetTicks) * 100f) + "%...", 100, MyFontEnum.Red);

                    if(holdPress >= removeTargetTicks)
                    {
                        holdPress = 0;
                        cooldown = cooldownTicks;
                        VoxelAction(PacketType.REMOVE_VOXEL, voxels, placeShape, placeScale);
                        PlaySound("HudDeleteBlock");
                    }
                }
                else
                {
                    #region Check for obstructions
                    // This is how the game checks it and I can't prevent it; I must also do it myself to prevent wasting ammo.
                    highlightEnts.Clear();
                    var shapeBB = placeShape.GetWorldBoundary();
                    MyGamePruningStructure.GetTopMostEntitiesInBox(ref shapeBB, highlightEnts, MyEntityQueryType.Dynamic);
                    highlightEnts.RemoveAll(e => !(e is IMyCubeGrid || e is IMyCharacter || e is IMyFloatingObject) || !e.PositionComp.WorldAABB.Intersects(shapeBB));
                    var localPlayerFound = highlightEnts.Remove((MyEntity)MyAPIGateway.Session.Player.Character);

                    if(localPlayerFound || highlightEnts.Count > 0)
                    {
                        PlaySound("HudUnable");

                        if(highlightEnts.Count == 0)
                        {
                            SetToolStatus("You're in the way!", 1500, MyFontEnum.Red);
                        }
                        else
                        {
                            SetToolStatus((localPlayerFound ? "You and " : "") + (highlightEnts.Count == 1 ? (localPlayerFound ? "one" : "One") + " thing " : highlightEnts.Count + " things ") + (localPlayerFound || highlightEnts.Count > 1 ? "are" : "is") + " in the way!", 1500, MyFontEnum.Red);
                        }

                        highlightEntsTicks = 0; // reset fadeout timer
                        cooldown = (cooldownTicks - recentActionTicks); // prevent quick retry, color the box and also prevent highlight by removing their ticks
                        holdPress = 0;
                        return false;
                    }

                    // don't clear highlightEnts because it's used to highlight them elsewhere
                    #endregion

                    cooldown = cooldownTicks;
                    VoxelAction(PacketType.PLACE_VOXEL, voxels, placeShape, placeScale, material.Index, Session.Player.Character);
                    PlaySound("HudPlaceBlock");
                    return true;
                }
            }
            else
            {
                holdPress = 0;
            }

            return false;
        }

        private void AimToCenter(IMyVoxelBase voxels, Vector3D forward)
        {
            var center = voxels.WorldAABB.Center;
            var dir = (placeMatrix.Translation - center);
            var altitude = dir.Normalize();
            placeMatrix = MatrixD.CreateFromDir(dir, forward);
            placeMatrix.Translation = center + (dir * altitude);
        }

        private static bool IsFaceVisible(Vector3D origin, Vector3 normal)
        {
            var dir = (origin - MyTransparentGeometry.Camera.Translation);
            return Vector3D.Dot(normal, dir) < 0;
        }

        private Vector3I RotateInput(bool newPressed)
        {
            Vector3I result;

            if(newPressed)
            {
                result.X = (Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE) ? -1 : (Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE) ? 1 : 0));
                result.Y = (Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE) ? -1 : (Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE) ? 1 : 0));
                result.Z = (Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE) ? -1 : (Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE) ? 1 : 0));
            }
            else
            {
                result.X = (Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE) ? -1 : (Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE) ? 1 : 0));
                result.Y = (Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE) ? -1 : (Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE) ? 1 : 0));
                result.Z = (Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE) ? -1 : (Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE) ? 1 : 0));
            }

            return result;
        }

        public void MessageEntered(string msg, ref bool send)
        {
            if(msg.StartsWith("/concrete", StringComparison.InvariantCultureIgnoreCase))
            {
                send = false;

                msg = msg.Substring("/concrete".Length).Trim();

                if(msg.StartsWith("help", StringComparison.InvariantCultureIgnoreCase))
                {
                    ShowHelp();
                    return;
                }

                Utilities.ShowMessage(Log.modName, "Commands:");
                Utilities.ShowMessage("/concrete help ", "key combination information");
            }
        }

        private void ShowHelp()
        {
            seenHelp = true;

            var inputFire = InputHandler.GetAssignedGameControlNames(MyControlsSpace.PRIMARY_TOOL_ACTION);
            var inputHelp = InputHandler.GetAssignedGameControlNames(MyControlsSpace.SECONDARY_TOOL_ACTION);
            var inputAlign = InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_DEFAULT_MOUNTPOINT);
            var inputSnap = InputHandler.GetAssignedGameControlNames(MyControlsSpace.FREE_ROTATION, true);
            var inputCycleMap = InputHandler.GetAssignedGameControlNames(MyControlsSpace.USE);
            string[] inputsRotation =
            {
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE, true),
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE, true),
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE, true),
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE, true),
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE, true),
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE, true),
            };

            var str = new StringBuilder();

            str.AppendLine("The concrete tool is a hand-held tool that allows placement of concrete\n  on to asteroids or planets.");
            str.AppendLine("The tool and ammo for it can be made in an assembler.");
            str.AppendLine("While holding the tool, you must be near a planet or asteroid to use it.");
            str.AppendLine();
            str.AppendLine("Controls:");
            str.AppendLine();
            str.Append(inputFire).Append(" = place concrete.").AppendLine();
            str.AppendLine();
            str.Append("Ctrl+").Append(inputFire).Append(" (hold) = remove terrain.").AppendLine();
            str.AppendLine();
            str.Append("Shift+MouseScroll/Plus/Minus = adjust box scale.").AppendLine();
            str.AppendLine();
            str.Append("Ctrl+MouseScroll/Plus/Minus = adjust box distance.").AppendLine();
            str.AppendLine();
            str.Append(inputAlign).Append(" = cycles alignment mode: reset alignment / align towards\n  center of asteroid/planet / align with aimed at grid.").AppendLine();
            str.AppendLine();
            str.Append(inputSnap).Append(" = cycles snap mode: no snap / snap voxel grid / snap to altitude.").AppendLine();
            str.AppendLine();
            str.Append("Shift+").Append(inputSnap).Append(" = depending on snap mode: lock to axis/plane / lock to altitude.").AppendLine();
            str.AppendLine();
            str.Append(inputCycleMap).Append(" = cycle between overlapping voxel maps.").AppendLine();
            str.AppendLine();
            str.Append(string.Join(",", inputsRotation)).Append(" = rotate the box.").AppendLine();
            str.AppendLine("  ...+Alt = rotate 1 degree increments.");
            str.AppendLine("  ...+Shift = rotate 15 degree increments.");
            str.AppendLine("  ...+Ctrl = rotate 90 degree increments.");
            str.AppendLine();
            str.Append(inputHelp).Append(" = show this window.").AppendLine();

            Utilities.ShowMissionScreen("Concrete Tool Help", string.Empty, string.Empty, str.ToString(), null, "Close");
        }
    }
}
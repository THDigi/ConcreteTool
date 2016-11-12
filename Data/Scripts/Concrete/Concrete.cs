using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRageMath;
using VRage;
using VRage.ModAPI;
using VRage.Input;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Voxels;

namespace Digi.Concrete
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Concrete : MySessionComponentBase
    {
        public override void LoadData()
        {
            Log.SetUp("Concrete Tool", 396679430, "ConcreteTool");
        }
        
        public enum PlaceShape
        {
            BOX,
            SPHERE,
            CAPSULE,
            RAMP,
        }

        public static bool init { get; private set; }
        public static bool isThisDedicated { get; private set; }

        private int skipClean = 0;
        private bool holdingTool = false;
        private IMyHudNotification toolStatus = null;
        private long lastShotTime = 0;
        //private Color prevCrosshairColor;

        private MyVoxelMaterialDefinition material = null;
        private MyStorageData cache = new MyStorageData();
        private HashSet<IMyEntity> ents = new HashSet<IMyEntity>();
        private Queue<string> voxelPackets = new Queue<string>();

        public const ushort PACKET = 63311;

        public const string CONCRETE_MATERIAL = "Concrete";
        public const string CONCRETE_TOOL = "ConcreteTool";
        public const string CONCRETE_AMMO_ID = "ConcreteMix";
        public const string CONCRETE_GHOST_ID = "ConcreteToolGhost";
        private static readonly MyObjectBuilder_AmmoMagazine CONCRETE_MAG = new MyObjectBuilder_AmmoMagazine() { SubtypeName = CONCRETE_AMMO_ID, ProjectilesCount = 1 };
        private static readonly MyDefinitionId CONCRETE_MAG_DEFID = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), CONCRETE_AMMO_ID);

        public const float CONCRETE_USE_PER_METER_SQUARE = 1f;

        private const int DRAW_WIREFRAME_TICKS = 60;

        private const long DELAY_SHOOT = (TimeSpan.TicksPerMillisecond * 100);

        //private static readonly Vector4 BOX_COLOR = new Vector4(0.0f, 0.0f, 1.0f, 0.05f);

        private static Color CROSSHAIR_INVALID = new Color(255, 0, 0);
        private static Color CROSSHAIR_VALID = new Color(0, 255, 0);
        private static Color CROSSHAIR_BLOCKED = new Color(255, 255, 0);

        public void Init()
        {
            Log.Init();

            init = true;
            isThisDedicated = (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer);
            
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;

            if(MyAPIGateway.Multiplayer.IsServer)
                MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET, ReceivedPacket);

            if(material == null && !MyDefinitionManager.Static.TryGetVoxelMaterialDefinition(CONCRETE_MATERIAL, out material))
            {
                throw new Exception("ERROR: Could not get the '" + CONCRETE_MATERIAL + "' voxel material!");
            }
        }

        protected override void UnloadData()
        {
            try
            {
                if(init)
                {
                    init = false;
                    
                    MyAPIGateway.Utilities.MessageEntered -= MessageEntered;

                    if(MyAPIGateway.Multiplayer.IsServer)
                        MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET, ReceivedPacket);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Log.Close();
        }

        public void ReceivedPacket(byte[] bytes)
        {
            try
            {
                int index = 0;

                var type = bytes[index];
                index += sizeof(byte);

                long entId = BitConverter.ToInt64(bytes, index);
                index += sizeof(long);

                if(!MyAPIGateway.Entities.EntityExists(entId))
                    return;

                var ent = MyAPIGateway.Entities.GetEntityById(entId) as MyEntity;
                var inv = ent.GetInventory(0) as IMyInventory;

                if(inv == null)
                    return;

                if(type == 0)
                {
                    if(inv.GetItemAmount(CONCRETE_MAG_DEFID) > 1) // don't add ammo if it's low because it can cause an infinite ammo glitch
                    {
                        inv.AddItems((MyFixedPoint)1, CONCRETE_MAG);
                    }
                }
                else
                {
                    float scale = BitConverter.ToSingle(bytes, index);
                    index += sizeof(float);

                    // scale ammo usage with placement size
                    inv.RemoveItemsOfType((MyFixedPoint)(CONCRETE_USE_PER_METER_SQUARE * scale), CONCRETE_MAG, false);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_Ammo(long entId, bool addItem)
        {
            try
            {
                int len = sizeof(byte) + sizeof(long);

                if(!addItem)
                    len += sizeof(float);

                var bytes = new byte[len];
                bytes[0] = (byte)(addItem ? 0 : 1);
                len = sizeof(byte);

                var data = BitConverter.GetBytes(entId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                if(!addItem)
                {
                    data = BitConverter.GetBytes(placeScale);
                    Array.Copy(data, 0, bytes, len, data.Length);
                    len += data.Length;
                }

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
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

                if(ents.Count > 0)
                {
                    if(++skipClean >= DRAW_WIREFRAME_TICKS)
                    {
                        ents.Clear();
                        skipClean = 0;
                    }
                    else
                    {
                        var color = Color.Red * 0.5f;

                        foreach(var ent in ents)
                        {
                            var matrix = ent.WorldMatrix;
                            var box = (BoundingBoxD)ent.LocalAABB;
                            MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref box, ref color, MySimpleObjectRasterizer.Wireframe, 1, 0.01f, "Square", "Square", false);
                        }
                    }
                }
                
                var character = MyAPIGateway.Session.ControlledObject as IMyCharacter;

                if(character != null)
                {
                    var charObj = character.GetObjectBuilder(false) as MyObjectBuilder_Character;
                    var tool = charObj.HandWeapon as MyObjectBuilder_AutomaticRifle;

                    if(tool != null && tool.SubtypeName == CONCRETE_TOOL)
                    {
                        if(!holdingTool)
                        {
                            DrawTool();
                            lastShotTime = tool.GunBase.LastShootTime;
                        }

                        if(tool.GunBase.LastShootTime > lastShotTime)
                        {
                            lastShotTime = tool.GunBase.LastShootTime;

                            if(!MyAPIGateway.Session.CreativeMode)
                            {
                                SendToServer_Ammo(character.EntityId, true); // always add the shot ammo back
                            }
                        }

                        bool trigger = (tool.GunBase.LastShootTime + DELAY_SHOOT) > DateTime.UtcNow.Ticks;
                        HoldingTool(trigger);
                        return;
                    }
                }

                if(holdingTool)
                {
                    HolsterTool();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void SetCrosshairColor(Color? color)
        {
            // HACK whitelist
            //MyHud.Crosshair.AddTemporarySprite(MyHudTexturesEnum.crosshair, MyStringId.GetOrCompute("Default"), 1000, 500, color, 0.02f);
        }

        public static void PlaySound(string name, float volume)
        {
            var emitter = new MyEntity3DSoundEmitter(MyAPIGateway.Session.ControlledObject.Entity as MyEntity);
            emitter.CustomVolume = volume;
            emitter.PlaySingleSound(new MySoundPair(name));
        }

        public void DrawTool()
        {
            holdingTool = true;

            if(toolStatus == null)
            {
                toolStatus = MyAPIGateway.Utilities.CreateNotification("", 500, MyFontEnum.White);
                toolStatus.Hide();
            }

            SetToolStatus("For key combination help, type in chat: /concrete help", MyFontEnum.Blue, 1000);
        }

        public void HoldingTool(bool trigger)
        {
            IMyVoxelBase voxelBase = GetVoxelMapAt(MyAPIGateway.Session.Player.GetPosition());
            bool placed = false;

            SetCrosshairColor(CROSSHAIR_INVALID);

            if(voxelBase == null)
            {
                if(trigger)
                {
                    SetToolStatus("Concrete can only be placed on planets or asteroids!", MyFontEnum.Red, 1500);
                }
            }
            else
            {
                placed = ToolProcess(voxelBase, trigger);
            }

            if(trigger && placed && !MyAPIGateway.Session.CreativeMode)
            {
                var character = MyAPIGateway.Session.ControlledObject as IMyCharacter;
                SendToServer_Ammo(character.EntityId, false); // expend the ammo manually
            }
        }

        public void HolsterTool()
        {
            holdingTool = false;

            SetCrosshairColor(null);

            if(toolStatus != null)
            {
                toolStatus.Hide();
            }
        }

#if STABLE // HACK >>> STABLE condition
        private void SetToolStatus(string text, MyFontEnum font, int aliveTime = 300)
#else
        private void SetToolStatus(string text, string font, int aliveTime = 300)
#endif
        {
            toolStatus.Font = font;
            toolStatus.Text = text;
            toolStatus.AliveTime = aliveTime;
            toolStatus.Show();
        }

        //private PlaceShape selectedShape = PlaceShape.BOX;
        private byte snap = 1;
        private float placeDistance = 4f;
        private float placeScale = 1f;
        private MatrixD placeMatrix = MatrixD.Identity;
        private bool rotated = false;
        private int holdPressRemove = 0;
        private byte cooldown = 0;
        private int altitudeLock = 0;
        private bool hudUnablePlayed = false;
        private byte skipSoundTicks = 0;

        private bool ToolProcess(IMyVoxelBase voxels, bool trigger)
        {
            var controlled = MyAPIGateway.Session.ControlledObject;
            var view = controlled.GetHeadMatrix(true, true);
            var target = view.Translation + (view.Forward * placeDistance);
            var planet = voxels as MyPlanet;
            IMyVoxelShape placeShape = null;

            var input = MyAPIGateway.Input;
            bool inputReadable = true; // InputHandler.IsInputReadable();
            // HACK undo this comment ^ once InputHandler.IsInputReadable() no longer throws exceptions when not in a menu; also remove all other InputHandler.IsInputReadable() instances from the code
            bool removeMode = false;

            if(inputReadable)
            {
                var scroll = input.DeltaMouseScrollWheelValue();
                removeMode = input.IsAnyCtrlKeyPressed();

                if(scroll != 0 && InputHandler.IsInputReadable())
                {
                    if(input.IsAnyCtrlKeyPressed())
                    {
                        if(scroll > 0)
                            placeDistance += 0.2f;
                        else
                            placeDistance -= 0.2f;

                        placeDistance = MathHelper.Clamp(placeDistance, 2f, 6f);

                        PlaySound("HudItem", 0.1f);

                        SetToolStatus("Distance: " + Math.Round(placeDistance, 2), MyFontEnum.Green, 1500);
                    }
                    else if(input.IsAnyShiftKeyPressed())
                    {
                        if(scroll > 0)
                            placeScale += 0.25f;
                        else
                            placeScale -= 0.25f;

                        placeScale = MathHelper.Clamp(placeScale, 0.5f, 3f);

                        PlaySound("HudItem", 0.1f);

                        SetToolStatus("Scale: " + Math.Round(placeScale, 2), MyFontEnum.Green, 1500);
                    }
                    /* TODO shape?
                    else
                    {
                        switch(selectedShape)
                        {
                            case PlaceShape.BOX:
                                selectedShape = (scroll > 0 ? PlaceShape.SPHERE : PlaceShape.RAMP);
                                break;
                            case PlaceShape.SPHERE:
                                selectedShape = (scroll > 0 ? PlaceShape.CAPSULE : PlaceShape.BOX);
                                break;
                            case PlaceShape.CAPSULE:
                                selectedShape = (scroll > 0 ? PlaceShape.RAMP : PlaceShape.SPHERE);
                                break;
                            case PlaceShape.RAMP:
                                selectedShape = (scroll > 0 ? PlaceShape.BOX : PlaceShape.CAPSULE);
                                break;
                        }

                        SetToolStatus("Shape: " + selectedShape, MyFontEnum.Green, 1500);
                    }
                    */
                }

                if(input.IsNewGameControlPressed(MyControlsSpace.FREE_ROTATION) && InputHandler.IsInputReadable())
                {
                    if(++snap > 2)
                        snap = 0;

                    PlaySound("HudItem", 0.1f);

                    switch(snap)
                    {
                        case 0:
                            SetToolStatus("Snap mode disabled.", MyFontEnum.Green, 1500);
                            break;
                        case 1:
                            SetToolStatus("Snap mode set to voxel-grid.", MyFontEnum.Green, 1500);
                            break;
                        case 2:
                            SetToolStatus("Snap mode set to altitude.", MyFontEnum.Green, 1500);
                            break;
                    }
                }

                bool shift = input.IsAnyShiftKeyPressed();
                var rotateInput = RotateInput(shift);

                if((rotateInput.X != 0 || rotateInput.Y != 0 || rotateInput.Z != 0) && InputHandler.IsInputReadable())
                {
                    if(shift || ++skipSoundTicks > 15)
                    {
                        skipSoundTicks = 0;
                        PlaySound("HudRotateBlock", 0.1f);
                    }

                    rotated = true;

                    float angle = (shift ? 15f : (input.IsAnyAltKeyPressed() ? 0.1f : 1));

                    if(rotateInput.X != 0)
                        placeMatrix *= MatrixD.CreateFromAxisAngle(placeMatrix.Up, MathHelper.ToRadians(rotateInput.X * angle));

                    if(rotateInput.Y != 0)
                        placeMatrix *= MatrixD.CreateFromAxisAngle(placeMatrix.Left, MathHelper.ToRadians(rotateInput.Y * angle));

                    if(rotateInput.Z != 0)
                        placeMatrix *= MatrixD.CreateFromAxisAngle(placeMatrix.Forward, MathHelper.ToRadians(rotateInput.Z * angle));
                }

                if(input.IsNewGameControlPressed(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE) && InputHandler.IsInputReadable())
                {
                    PlaySound("HudItem", 0.1f);

                    var grid = MyAPIGateway.CubeBuilder.FindClosestGrid();

                    if(grid != null)
                    {
                        placeMatrix = grid.WorldMatrix;
                        rotated = true;

                        SetToolStatus("Aligned with selected grid.", MyFontEnum.Green, 1500);
                    }
                    else
                    {
                        if(rotated)
                        {
                            placeMatrix.Up = Vector3D.Up;
                            placeMatrix.Right = Vector3D.Right;
                            placeMatrix.Forward = Vector3D.Forward;
                            rotated = false;

                            SetToolStatus("Alignement reset.", MyFontEnum.Green, 1500);
                        }
                        else
                        {
                            var center = (planet != null ? planet.WorldMatrix.Translation : voxels.PositionLeftBottomCorner + (voxels.Storage.Size / 2)); var dir = (target - center);
                            var altitude = Math.Round(dir.Normalize(), 0);
                            target = center + (dir * altitude);

                            placeMatrix = MatrixD.CreateFromDir(dir, view.Forward);
                            rotated = true;

                            SetToolStatus((planet != null ? "Aligned towards center of planet." : "Aligned towards center of asteroid."), MyFontEnum.Green, 1500);
                        }
                    }
                }
            }

            if(snap == 0) // no snapping
            {
                placeMatrix.Translation = target;
            }
            else if(snap == 1) // snap to voxel grid
            {
                target = voxels.PositionLeftBottomCorner + Vector3I.Round(target - voxels.PositionLeftBottomCorner);
                placeMatrix.Translation = target;

                var gridColor = Color.Wheat * 0.25f;
                const float gridLineWidth = 0.01f;
                const float gridLineLength = 3f;
                const float gridLineLengthHalf = gridLineLength / 2;
                const string gridLineMaterial = "ConcreteTool_FadeOutLine";

                var upHalf = (Vector3.Up / 2);
                var rightHalf = (Vector3.Right / 2);
                var forwardHalf = (Vector3.Forward / 2);

                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + upHalf + -rightHalf + Vector3.Forward * gridLineLengthHalf, Vector3.Backward, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + upHalf + rightHalf + Vector3.Forward * gridLineLengthHalf, Vector3.Backward, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + -upHalf + -rightHalf + Vector3.Forward * gridLineLengthHalf, Vector3.Backward, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + -upHalf + rightHalf + Vector3.Forward * gridLineLengthHalf, Vector3.Backward, gridLineLength, gridLineWidth);

                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + forwardHalf + -rightHalf + Vector3.Up * gridLineLengthHalf, Vector3.Down, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + forwardHalf + rightHalf + Vector3.Up * gridLineLengthHalf, Vector3.Down, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + -forwardHalf + -rightHalf + Vector3.Up * gridLineLengthHalf, Vector3.Down, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + -forwardHalf + rightHalf + Vector3.Up * gridLineLengthHalf, Vector3.Down, gridLineLength, gridLineWidth);

                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + forwardHalf + -upHalf + Vector3.Right * gridLineLengthHalf, Vector3.Left, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + forwardHalf + upHalf + Vector3.Right * gridLineLengthHalf, Vector3.Left, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + -forwardHalf + -upHalf + Vector3.Right * gridLineLengthHalf, Vector3.Left, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + -forwardHalf + upHalf + Vector3.Right * gridLineLengthHalf, Vector3.Left, gridLineLength, gridLineWidth);
            }
            else if(snap == 2) // snap to distance increments from center
            {
                var center = (planet != null ? planet.WorldMatrix.Translation : voxels.PositionLeftBottomCorner + (voxels.Storage.Size / 2));
                var dir = (target - center);
                int altitude = (int)Math.Round(dir.Normalize(), 0);
                target = center + (dir * altitude);

                if(inputReadable && input.IsAnyShiftKeyPressed() && InputHandler.IsInputReadable())
                {
                    if(input.IsNewKeyPressed(MyKeys.Shift))
                        altitudeLock = altitude;

                    if(Math.Abs(altitude - altitudeLock) > 3)
                    {
                        if(!hudUnablePlayed)
                        {
                            PlaySound("HudUnable", 0.1f);
                            hudUnablePlayed = true;
                        }

                        MyAPIGateway.Utilities.ShowNotification("Too far away from locked altitude!", 16, MyFontEnum.Red);
                        return false;
                    }

                    target = center + (dir * altitudeLock);

                    MyAPIGateway.Utilities.ShowNotification("Locked to altitude.", 16, MyFontEnum.Blue);

                    hudUnablePlayed = false;
                }

                placeMatrix.Translation = target;

                var gridColor = Color.Wheat * 0.25f;
                const float gridLineWidth = 0.01f;
                const float gridLineLength = 3f;
                const float gridLineLengthHalf = gridLineLength / 2;
                const string gridLineMaterial = "ConcreteTool_FadeOutLine";

                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + (dir * 3.75), -dir, 7.5f, gridLineWidth);

                var vertical = Vector3D.Cross(dir, view.Forward);

                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target - (dir * 1.5) + vertical * gridLineLengthHalf, -vertical, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target - (dir * 0.5) + vertical * gridLineLengthHalf, -vertical, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + (dir * 0.5) + vertical * gridLineLengthHalf, -vertical, gridLineLength, gridLineWidth);
                MyTransparentGeometry.AddLineBillboard(gridLineMaterial, gridColor, target + (dir * 1.5) + vertical * gridLineLengthHalf, -vertical, gridLineLength, gridLineWidth);
            }

            var colorWire = Color.Green * 0.5f;
            var colorFace = Color.Gray * 0.1f;

            const byte cooldownTicks = 15;
            const byte cooldownTicksGlow = 10;
            const int removeTargetTicks = 15;
            //int removeTargetTicks = (int)(30 * placeScale);

            if(removeMode)
                colorWire = (holdPressRemove > 0 ? Color.Lerp(Color.Red, Color.OrangeRed, ((float)holdPressRemove / (float)removeTargetTicks)) : Color.Red) * 0.5f;
            else if(cooldown >= cooldownTicksGlow)
                colorWire = Color.Blue * 0.5f;
            else if(cooldown > 0)
                colorWire = Color.White * 0.1f;

            {
                var shape = MyAPIGateway.Session.VoxelMaps.GetBoxVoxelHand();
                var vec = (Vector3D.One / 2) * placeScale;
                var box = new BoundingBoxD(-vec, vec);
                shape.Boundaries = box;
                placeShape = shape;

                MySimpleObjectDraw.DrawTransparentBox(ref placeMatrix, ref box, ref colorWire, MySimpleObjectRasterizer.Wireframe, 1, 0.01f, "Square", "Square", false);
                MySimpleObjectDraw.DrawTransparentBox(ref placeMatrix, ref box, ref colorFace, MySimpleObjectRasterizer.Solid, 1, 0.01f, "Square", "Square", false);
            }

            // TODO shapes?
            /*
            switch(selectedShape)
            {
                default: // BOX
                    {
                        // box code here
                        break;
                    }
                case PlaceShape.SPHERE:
                    {
                        var shape = MyAPIGateway.Session.VoxelMaps.GetSphereVoxelHand();
                        shape.Radius = placeScale;
                        placeShape = shape;

                        MySimpleObjectDraw.DrawTransparentSphere(ref placeMatrix, shape.Radius, ref colorWire, MySimpleObjectRasterizer.Wireframe, 12, "Square", "Square", 0.01f);
                        MySimpleObjectDraw.DrawTransparentSphere(ref placeMatrix, shape.Radius, ref colorFace, MySimpleObjectRasterizer.Solid, 12, "Square", "Square", 0.01f);
                        break;
                    }
                case PlaceShape.CAPSULE:
                    {
                        var shape = MyAPIGateway.Session.VoxelMaps.GetCapsuleVoxelHand();
                        shape.Radius = placeScale;
                        placeShape = shape;

                        MySimpleObjectDraw.DrawTransparentCapsule(ref placeMatrix, shape.Radius, 2, ref colorWire, 12, "Square");
                        break;
                    }
                case PlaceShape.RAMP:
                    {
                        var shape = MyAPIGateway.Session.VoxelMaps.GetRampVoxelHand();
                        shape.RampNormal = Vector3D.Forward; // TODO direction
                        shape.RampNormalW = placeScale;
                        placeShape = shape;

                        var vec = (Vector3D.One / 2) * placeScale;
                        var box = new BoundingBoxD(-vec, vec);

                        MySimpleObjectDraw.DrawTransparentRamp(ref placeMatrix, ref box, ref colorWire, "Square");
                        break;
                    }
            }
            */

            if(cooldown > 0 && --cooldown > 0)
                return false;

            if(trigger)
            {
                placeShape.Transform = placeMatrix;

                if(removeMode)
                {
                    holdPressRemove++;

                    if(holdPressRemove % 3 == 0)
                        SetToolStatus("Removing " + (int)(((float)holdPressRemove / (float)removeTargetTicks) * 100f) + "%...", MyFontEnum.Red, 500);

                    if(holdPressRemove >= removeTargetTicks)
                    {
                        MyAPIGateway.Session.VoxelMaps.CutOutShape(voxels, placeShape);
                        PlaySound("HudDeleteBlock", 0.1f);
                        cooldown = cooldownTicks;
                        holdPressRemove = 0;
                    }

                    return false; // don't use ammo for removal
                }
                else
                {
                    var box = placeShape.GetWorldBoundary();
                    ents.Clear();
                    MyAPIGateway.Entities.GetEntities(ents, (ent => ent.Physics != null && !ent.Physics.IsStatic && (ent is IMyCubeGrid || ent is IMyFloatingObject || ent is IMyCharacter) && ent.WorldAABB.Intersects(ref box)));
                    int found = ents.Count;

                    if(found > 0)
                    {
                        bool localPlayerFound = ents.RemoveWhere((e) => e.EntityId == controlled.Entity.EntityId) > 0;

                        SetCrosshairColor(CROSSHAIR_BLOCKED);
                        SetToolStatus((found == 1 ? (localPlayerFound ? "You're in the way!" : "Something is in the way!") : (localPlayerFound ? "You and " + (ents.Count - 1) : "" + ents.Count) + " things are in the way!"), MyFontEnum.Red, 1500);

                        if(!hudUnablePlayed)
                        {
                            PlaySound("HudUnable", 0.1f);
                            hudUnablePlayed = true;
                        }

                        return false;
                    }

                    hudUnablePlayed = false;

                    MyAPIGateway.Session.VoxelMaps.FillInShape(voxels, placeShape, material.Index);
                    PlaySound("HudPlaceBlock", 0.1f);
                    cooldown = cooldownTicks;
                    holdPressRemove = 0;
                    return true;
                }
            }
            else
            {
                hudUnablePlayed = false;
            }

            holdPressRemove = 0;
            return false; // don't use ammo
        }

        private Vector3I RotateInput(bool newPressed)
        {
            var input = MyAPIGateway.Input;

            if(newPressed)
            {
                var x = (input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE) ? -1 : (input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE) ? 1 : 0));
                var y = (input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE) ? -1 : (input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE) ? 1 : 0));
                var z = (input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE) ? -1 : (input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE) ? 1 : 0));

                return new Vector3I(x, y, z);
            }
            else
            {
                var x = (input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE) ? -1 : (input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE) ? 1 : 0));
                var y = (input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE) ? -1 : (input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE) ? 1 : 0));
                var z = (input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE) ? -1 : (input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE) ? 1 : 0));

                return new Vector3I(x, y, z);
            }
        }

        private IMyVoxelBase GetVoxelMapAt(Vector3D pos)
        {
            var maps = new List<IMyVoxelBase>();
            MyAPIGateway.Session.VoxelMaps.GetInstances(maps);
            Vector3D min;
            Vector3D max;

            // TODO get the smallest of the matches? or better yet just get all of them and give the player a choice

            foreach(var voxel in maps)
            {
                if(voxel.StorageName == null)
                    continue;

                min = voxel.PositionLeftBottomCorner;
                max = min + voxel.Storage.Size;

                if(min.X <= pos.X && pos.X <= max.X && min.Y <= pos.Y && pos.Y <= max.Y && min.Z <= pos.Z && pos.Z <= max.Z)
                    return voxel;
            }

            return null;
        }

        public void MessageEntered(string msg, ref bool send)
        {
            if(msg.StartsWith("/concrete", StringComparison.InvariantCultureIgnoreCase))
            {
                send = false;

                msg = msg.Substring("/concrete".Length).Trim();

                if(msg.StartsWith("help", StringComparison.InvariantCultureIgnoreCase))
                {
                    var inputFire = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.PRIMARY_TOOL_ACTION));
                    var inputAlign = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE));
                    var inputGrid = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.FREE_ROTATION));
                    var str = new StringBuilder();

                    str.AppendLine("The concrete tool is a hand-held tool that allows\n  placement of concrete on to asteroids or planets.");
                    str.AppendLine();
                    str.AppendLine("The tool and ammo for it can be made in an assembler.");
                    str.AppendLine();
                    str.AppendLine("While holding the tool, you must be near a planet or asteroid to use it");
                    str.AppendLine();
                    str.AppendLine("You can use the following inputs to control it:");
                    str.AppendLine();
                    str.Append(inputFire).Append(" = place concrete.").AppendLine();
                    str.AppendLine();
                    str.Append("Ctrl + ").Append(inputFire).Append(" (hold) = delete voxels.").AppendLine();
                    str.AppendLine();
                    str.Append("Shift + MouseScroll = adjust box scale").AppendLine();
                    str.AppendLine();
                    str.Append("Ctrl + MouseScroll = adjust box distance").AppendLine();
                    str.AppendLine();
                    str.Append(inputAlign).Append(" = alignment mode: reset alignment / align towards\n  center of asteroid/planet / align with aimed at grid.").AppendLine();
                    str.AppendLine();
                    str.Append(inputGrid).Append(" = snap mode: no snap / snap voxel grid / snap to\n  altitude.").AppendLine();
                    str.AppendLine();
                    str.AppendLine("While in 'snap to altitude' mode you can hold Shift to lock\n  to the current altitude.");
                    str.AppendLine();
                    str.AppendLine("Use the cube rotation keys to rotate the placement box.");
                    str.AppendLine("They can also be used with Shift for 15 degree increments\n  or Alt for 0.1 degree increments instead of 1.");

                    MyAPIGateway.Utilities.ShowMissionScreen("Concrete Tool Help", null, null, str.ToString(), null, "Close");
                    return;
                }

                MyAPIGateway.Utilities.ShowMessage(Log.modName, "Commands:");
                MyAPIGateway.Utilities.ShowMessage("/concrete help ", "key combination information");
            }
        }
    }
}
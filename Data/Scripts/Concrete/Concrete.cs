using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Common.Utils;
using VRageMath;
using VRage;
using VRage.ObjectBuilders;
using VRage.Voxels;
using VRage.ModAPI;
using Digi.Utils;

namespace Digi.Concrete
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Concrete : MySessionComponentBase
    {
        public static bool init { get; private set; }
        public static bool isServer { get; private set; }
        public static bool isDedicated { get; private set; }
        
        private int skipPackets = 0;
        private int skipClean = 0;
        private int retries = 0;
        private bool holdingTool = false;
        private IMyEntity cursor = null;
        private MyVoxelMaterialDefinition material = null;
        private MyStorageDataCache cache = new MyStorageDataCache();
        private HashSet<IMyEntity> ents = new HashSet<IMyEntity>();
        private Queue<string> voxelPackets = new Queue<string>();
        
        public const byte VOXEL_OVERWRITE = 159;
        public const ushort PACKET_VOXELS = 63311;
        public static readonly Encoding encode = Encoding.Unicode;
        
        public const string CONCRETE_MATERIAL = "Concrete";
        public const string CONCRETE_TOOL = "ConcreteTool";
        public const string CONCRETE_AMMO_NAME = "Concrete Mix";
        public const string CONCRETE_AMMO_ID = "ConcreteMix";
        public const string CONCRETE_AMMO_TYPEID = "MyObjectBuilder_AmmoMagazine/ConcreteMix";
        
        private const int SKIP_TICKS_CLEAN = 30;
        private const int SKIP_TICKS_PACKETS = 60;
        
        private static readonly Vector4 BOX_COLOR = new Vector4(0.0f, 0.0f, 1.0f, 0.05f);
        private static readonly Vector3 MISSILE_SIZE = new Vector3(0.001, 0.001, 0.001);
        
        public void Init()
        {
            Log.Info("Initialized.");
            init = true;
            isServer = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer;
            isDedicated = (MyAPIGateway.Utilities.IsDedicated && isServer);
            
            cache.Resize(Vector3I.One);
            
            MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET_VOXELS, ReceivedVoxels);
            
            if(material == null && !MyDefinitionManager.Static.TryGetVoxelMaterialDefinition(CONCRETE_MATERIAL, out material))
            {
                throw new Exception("ERROR: Could not get the '"+CONCRETE_MATERIAL+"' voxel material!");
            }
        }
        
        protected override void UnloadData()
        {
            init = false;
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET_VOXELS, ReceivedVoxels);
            Log.Close();
        }
        
        public void ReceivedVoxels(byte[] bytes)
        {
            string receivedString = encode.GetString(bytes);
            
            Log.Info("DEBUG: ReceivedVoxel(); received:");
            Log.Info(receivedString);
            Log.Info(" ");
            
            string[] lines = receivedString.Split('\n');
            string[] data;
            int x;
            int y;
            int z;
            
            foreach(string line in lines)
            {
                data = line.Split(new char[] { ';' }, 4);
                
                if(data.Length != 4 || !int.TryParse(data[0], out x) || !int.TryParse(data[1], out y) || !int.TryParse(data[2], out z))
                {
                    Log.Error("Invalid data: " + data.ToString());
                    continue;
                }
                
                List<IMyVoxelBase> asteroids = new List<IMyVoxelBase>();
                MyAPIGateway.Session.VoxelMaps.GetInstances(asteroids, a => a.StorageName.Equals(data[3]));
                
                if(asteroids.Count == 0)
                {
                    Log.Error("Couldn't find asteroid: " + data[3]);
                    continue;
                }
                
                SetAsteroidVoxel(asteroids[0], new Vector3I(x, y, z));
                
                Log.Info("DEBUG: " + asteroids[0].StorageName + " set voxel at "+x+","+y+","+z);
            }
        }
        
        private void SendVoxelUpdates()
        {
            if(voxelPackets.Count == 0 && skipPackets == 0)
                return;
            
            if(++skipPackets < SKIP_TICKS_PACKETS)
                return;
            
            skipPackets = 0;
            
            if(voxelPackets.Count == 0)
                return;
            
            List<string> packet = new List<string>();
            int bytes = 0;
            
            while(voxelPackets.Count > 0)
            {
                bytes += (voxelPackets.Peek().Length * sizeof(char));
                
                if(bytes < 4096)
                {
                    packet.Add(voxelPackets.Dequeue());
                }
            }
            
            string data = String.Join("\n", packet);
            
            Log.Info("DEBUG: Sending voxels; bytes="+bytes+"; data:");
            Log.Info(data);
            Log.Info("");
            
            if(MyAPIGateway.Multiplayer.SendMessageToOthers(PACKET_VOXELS, encode.GetBytes(data), true))
            {
                retries = 0;
                
                if(voxelPackets.Count > 0)
                {
                    Log.Info("DEBUG: Packet sent but there are still " + voxelPackets.Count + " packets to process!");
                }
            }
            else
            {
                if(++retries > 3)
                {
                    Log.Info("ERROR: SendMessageToOthers() failed too many times, not retrying!");
                }
                else
                {
                    Log.Info("ERROR: SendMessageToOthers() failed! Packets re-queued ("+retries+").");
                    
                    foreach(string p in packet)
                    {
                        voxelPackets.Enqueue(p);
                    }
                }
            }
        }
        
        private void QueueUpdateVoxel(string asteroidName, Vector3I pos)
        {
            voxelPackets.Enqueue(String.Format("{0};{1};{2};{3}", pos.X, pos.Y, pos.Z, asteroidName));
        }
        
        public override void UpdateBeforeSimulation()
        {
            if(!init)
            {
                if(MyAPIGateway.Session == null)
                    return;
                
                Init();
            }
            
            SendVoxelUpdates();
            
            if(!isDedicated && MyAPIGateway.Session.Player != null && MyAPIGateway.Session.Player.Controller != null && MyAPIGateway.Session.Player.Controller.ControlledEntity != null && MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity != null)
            {
                CleanHitBoxes();
                
                var player = MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity;
                
                if(player is IMyCharacter)
                {
                    var character = player.GetObjectBuilder(false) as MyObjectBuilder_Character;
                    var tool = character.HandWeapon;
                    
                    if(tool != null && tool.SubtypeName == CONCRETE_TOOL)
                    {
                        if(!holdingTool)
                            DrawTool();
                        
                        bool trigger = !character.LightEnabled;
                        
                        if(trigger)
                        {
                            MyAPIGateway.Session.Player.Controller.ControlledEntity.SwitchLights();
                        }
                        
                        HoldingTool(trigger);
                        return;
                    }
                }
            }
            
            if(holdingTool)
            {
                HolsterTool();
            }
        }
        
        public void DrawTool()
        {
            holdingTool = true;
            MyAPIGateway.Utilities.ShowNotification("Press your LIGHTS key to trigger this tool!", 1500, MyFontEnum.Green);
        }
        
        public void HoldingTool(bool trigger)
        {
            IMyVoxelBase asteroid = GetAsteroidAt(MyAPIGateway.Session.Player.GetPosition());
            
            if(asteroid == null)
            {
                UpdateCursorAt(Vector3D.Zero);
                return;
            }
            
            ScanAndTrigger(asteroid, trigger, 4.0f);
        }
        
        public void HolsterTool()
        {
            holdingTool = false;
            
            if(cursor != null)
            {
                cursor.Close();
                cursor = null;
            }
        }
        
        private void ScanAndTrigger(IMyVoxelBase asteroid, bool trigger, float meters, bool first = true)
        {
            Vector3D target = GetTargetAt(meters);
            Vector3I pos = AdjustTargetForAsteroid(asteroid, ref target);
            UpdateCursorAt(target);
            byte voxel = GetAsteroidVoxel(asteroid, pos);
            
            if(voxel == 0 || voxel < VOXEL_OVERWRITE)
            {
                if(meters == 4.0f)
                {
                    if(trigger)
                        MyAPIGateway.Utilities.ShowNotification("Aim at an asteroid surface closer.", 1500, MyFontEnum.Red);
                    
                    UpdateCursorAt(Vector3D.Zero);
                    return;
                }
                
                int index = -1;
                
                if(trigger && CheckAmmo(ref index) && !IsTargetBlocked(target))
                {
                    UseAmmo(index);
                    SetAsteroidVoxel(asteroid, pos);
                    QueueUpdateVoxel(asteroid.StorageName, pos);
                }
            }
            else
            {
                if(meters <= 1.0f)
                {
                    if(trigger)
                        MyAPIGateway.Utilities.ShowNotification("Move away from the surface to be able to pour concrete.", 1500, MyFontEnum.Red);
                    
                    UpdateCursorAt(Vector3D.Zero);
                    return;
                }
                
                ScanAndTrigger(asteroid, trigger, meters - 1.0f, false);
            }
        }
        
        private bool CheckAmmo(ref int index)
        {
            //if(MyAPIGateway.Session.CreativeMode)
            //    return true;
            
            var invOwner = MyAPIGateway.Session.Player.Controller.ControlledEntity as Sandbox.ModAPI.Interfaces.IMyInventoryOwner;
            var inv = invOwner.GetInventory(0) as Sandbox.ModAPI.IMyInventory;
            var items = inv.GetItems();
            
            if(items.Count > 0)
            {
                IMyInventoryItem item;
                
                for(int i = 0; i < items.Count; i++)
                {
                    item = items[i];
                    
                    if(item.Content.SubtypeName == CONCRETE_AMMO_ID && (double)item.Amount >= 1.0)
                    {
                        index = i;
                        return true;
                    }
                }
            }
            
            MyAPIGateway.Utilities.ShowNotification("You need " + CONCRETE_AMMO_NAME + " to use this tool.", 1500, MyFontEnum.Red);
            return false;
        }
        
        private void UseAmmo(int index)
        {
            //if(MyAPIGateway.Session.CreativeMode)
            //    return;
            
            var invOwner = MyAPIGateway.Session.Player.Controller.ControlledEntity as Sandbox.ModAPI.Interfaces.IMyInventoryOwner;
            var inv = invOwner.GetInventory(0) as Sandbox.ModAPI.IMyInventory;
            inv.RemoveItemsAt(index, (MyFixedPoint)1.0, true, false);
        }
        
        private IMyVoxelBase GetAsteroidAt(Vector3D pos)
        {
            var asteroids = new List<IMyVoxelBase>();
            MyAPIGateway.Session.VoxelMaps.GetInstances(asteroids);
            Vector3D min;
            Vector3D max;
            
            foreach(IMyVoxelBase asteroid in asteroids)
            {
                min = asteroid.PositionLeftBottomCorner;
                max = min + asteroid.Storage.Size;
                
                if(min.X <= pos.X && pos.X <= max.X && min.Y <= pos.Y && pos.Y <= max.Y && min.Z <= pos.Z && pos.Z <= max.Z)
                {
                    return asteroid;
                }
            }
            
            return null;
        }
        
        private Vector3D GetTargetAt(float meters)
        {
            var player = MyAPIGateway.Session.ControlledObject;
            var view = player.GetHeadMatrix(true, true);
            return view.Translation + (view.Forward * meters) + (Vector3D.One * 0.5);
            //return player.Entity.WorldAABB.Center + (view.Forward * meters) + (view.Up * 0.75);
        }
        
        private void UpdateCursorAt(Vector3D target)
        {
            if(cursor == null)
            {
                var prefab = MyDefinitionManager.Static.GetPrefabDefinition("Ghost");
                
                if(prefab == null)
                    return;
                
                if(prefab.CubeGrids == null)
                {
                    MyDefinitionManager.Static.ReloadPrefabsFromFile(prefab.PrefabPath);
                    prefab = MyDefinitionManager.Static.GetPrefabDefinition("Ghost");
                }
                
                MyObjectBuilder_CubeGrid builder = prefab.CubeGrids[0].Clone() as MyObjectBuilder_CubeGrid;
                builder.PersistentFlags = MyPersistentEntityFlags2.InScene;
                builder.Name = "";
                builder.DisplayName = "";
                builder.CreatePhysics = false;
                builder.PositionAndOrientation = new MyPositionAndOrientation(MyAPIGateway.Session.ControlledObject.Entity.WorldAABB.Center, Vector3D.Forward, Vector3D.Up);
                
                MyAPIGateway.Entities.RemapObjectBuilder(builder);
                cursor = MyAPIGateway.Entities.CreateFromObjectBuilder(builder);
                cursor.Flags &= ~EntityFlags.Sync; // client side only
                cursor.Flags &= ~EntityFlags.Save; // don't save this entity
                MyAPIGateway.Entities.AddEntity(cursor, true);
            }
            
            if(cursor != null)
            {
                cursor.SetPosition(target);
            }
        }
        
        private Vector3I AdjustTargetForAsteroid(IMyVoxelBase asteroid, ref Vector3D target)
        {
            var pos = new Vector3I(target - asteroid.PositionLeftBottomCorner);
            target = asteroid.PositionLeftBottomCorner + new Vector3D(pos);
            return pos;
        }
        
        private void CleanHitBoxes(bool noSkip = false)
        {
            if(ents.Count > 0)
            {
                if(noSkip || ++skipClean >= SKIP_TICKS_CLEAN)
                {
                    foreach(var ent in ents)
                    {
                        MyAPIGateway.Entities.EnableEntityBoundingBoxDraw(ent, false);
                    }
                    
                    ents.Clear();
                    skipClean = 0;
                }
            }
        }
        
        private bool IsTargetBlocked(Vector3D target)
        {
            CleanHitBoxes(true);
            BoundingBoxD box = new BoundingBoxD(target - 0.5, target + 0.5);
            MyAPIGateway.Entities.GetEntities(ents, (ent => ent.Physics != null && !ent.Physics.IsStatic && (ent is IMyCubeGrid || ent is IMyFloatingObject || ent is IMyCharacter) && ent.WorldAABB.Intersects(ref box)));
            
            if(ents.Count > 0)
            {
                bool you = false;
                
                foreach(var ent in ents)
                {
                    if(!you && ent == MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity)
                        you = true;
                    
                    MyAPIGateway.Entities.EnableEntityBoundingBoxDraw(ent, true, BOX_COLOR, 0.5f, null);
                }
                
                MyAPIGateway.Utilities.ShowNotification((ents.Count == 1 ? (you ? "You're in the way!" : "Something is in the way!") : (you ? "You and " + (ents.Count-1) : "" + ents.Count) + " things are in the way!"), 1500, MyFontEnum.Red);
                return true;
            }
            
            return false;
        }
        
        private byte GetAsteroidVoxel(IMyVoxelBase asteroid, Vector3I pos)
        {
            asteroid.Storage.ReadRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, 0, pos, pos);
            return cache.Content(ref Vector3I.Zero);
        }
        
        private void SetAsteroidVoxel(IMyVoxelBase asteroid, Vector3I pos)
        {
            cache.Content(ref Vector3I.Zero, MyVoxelConstants.VOXEL_CONTENT_FULL);
            cache.Material(ref Vector3I.Zero, material.Index);
            asteroid.Storage.WriteRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, pos, pos);
        }
    }
}
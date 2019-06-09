using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Digi.ConcreteTool.MP
{
    [ProtoContract]
    public class PacketVoxelAction : PacketBase
    {
        [ProtoMember(1)]
        private readonly VoxelActionEnum ActionType;

        [ProtoMember(2)]
        private readonly long VoxelEntId;

        [ProtoMember(3)]
        private readonly float Scale;

        [ProtoMember(4)]
        private readonly Vector3D Origin;

        [ProtoMember(5)]
        private readonly Quaternion Orientation;

        [ProtoMember(6)]
        private readonly long CharEntId;

        public PacketVoxelAction() { } // Empty constructor required for deserialization

        public PacketVoxelAction(VoxelActionEnum actionType, long voxelEntId, float scale, Vector3D origin, Quaternion orientation, long charEntId)
        {
            ActionType = actionType;
            VoxelEntId = voxelEntId;
            Scale = scale;
            Origin = origin;
            Orientation = orientation;
            CharEntId = charEntId;
        }

        public override bool Received()
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                return false;

            var voxelEnt = MyAPIGateway.Entities.GetEntityById(VoxelEntId) as IMyVoxelBase;

            if(voxelEnt == null)
            {
                Log.Error($"Received packet with wrong entity; entityId={VoxelEntId}; expected IMyVoxelBase got ={MyAPIGateway.Entities.GetEntityById(VoxelEntId)}; actionType={ActionType}");
                return false;
            }

            var shape = MyAPIGateway.Session.VoxelMaps.GetBoxVoxelHand();

            var vec = new Vector3D(Scale * 0.5);
            shape.Boundaries = new BoundingBoxD(-vec, vec);

            var m = MatrixD.CreateFromQuaternion(Orientation);
            m.Translation = Origin;
            shape.Transform = m;

            var character = MyAPIGateway.Entities.GetEntityById(CharEntId) as IMyCharacter;

            if(character == null)
            {
                Log.Error($"Received packet with unknown character entityId={CharEntId}; found={MyAPIGateway.Entities.GetEntityById(CharEntId)}; actionType={ActionType}");
                return false;
            }

            ConcreteToolMod.Instance.VoxelAction(ActionType, voxelEnt, shape, Scale, character);

            SendToolAction(); // tool sounds, etc

            return false; // don't relay packet
        }

        private void SendToolAction()
        {
            var packet = new PacketToolAction(ActionType, CharEntId, Scale, Origin);
            var packetBytes = MyAPIGateway.Utilities.SerializeToBinary(packet);

            var network = ConcreteToolMod.Instance.Network;
            var players = network.TempPlayers;
            players.Clear();
            MyAPIGateway.Players.GetPlayers(players);

            foreach(var p in players)
            {
                // server player needs this packet too
                //if(p.SteamUserId == MyAPIGateway.Multiplayer.ServerId)
                //    continue;

                if(p.SteamUserId == SenderId) // don't sends sounds back to sender
                    continue;

                if(p.Character == null || Vector3D.DistanceSquared(p.Character.GetPosition(), Origin) > ConcreteToolMod.TOOL_ACTION_MAX_DIST_SQ)
                    continue;

                MyAPIGateway.Multiplayer.SendMessageTo(network.PacketId, packetBytes, p.SteamUserId);
            }

            players.Clear();
        }
    }
}

using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.ConcreteTool.MP
{
    [ProtoContract]
    public class PacketToolAction : PacketBase
    {
        [ProtoMember]
        private readonly VoxelActionEnum ActionType;

        [ProtoMember]
        private readonly long CharEntId;

        [ProtoMember]
        private readonly float Scale;

        [ProtoMember]
        private readonly Vector3D Origin;

        public PacketToolAction() { } // Empty constructor required for deserialization

        public PacketToolAction(VoxelActionEnum type, long charEntId, float scale, Vector3D origin)
        {
            ActionType = type;
            CharEntId = charEntId;
            Scale = scale;
            Origin = origin;
        }

        public override bool Received()
        {
            var character = MyAPIGateway.Entities.GetEntityById(CharEntId) as IMyCharacter;

            if(character == null)
            {
                Log.Error($"Received toolaction packet with unknown character entityId={CharEntId}; found={MyAPIGateway.Entities.GetEntityById(CharEntId)}; actionType={ActionType}");
                return false;
            }

            ConcreteToolMod.ToolAction(ActionType, character, Scale, Origin);

            return false; // no relaying because it's sent to server player too and will cause duplication
        }
    }
}
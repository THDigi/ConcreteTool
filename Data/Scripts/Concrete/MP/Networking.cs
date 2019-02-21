using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Digi.ConcreteTool.MP
{
    public class Networking
    {
        public readonly ushort PacketId;

        public readonly List<IMyPlayer> TempPlayers = new List<IMyPlayer>();

        public Networking(ushort packetId)
        {
            PacketId = packetId;
        }

        public void Register()
        {
            MyAPIGateway.Multiplayer.RegisterMessageHandler(PacketId, ReceivedPacket);
        }

        public void Unregister()
        {
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(PacketId, ReceivedPacket);
        }

        private void ReceivedPacket(byte[] rawData)
        {
            try
            {
                if(rawData.Length <= 2)
                    return; // invalid packet

                var packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(rawData);

                var relay = packet.Received();

                if(relay)
                    RelayToClients(packet, rawData);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        /// <summary>
        /// Sends packet (or supplied bytes) to all players except server player and supplied packet's sender.
        /// Only works server side.
        /// </summary>
        public void RelayToClients(PacketBase packet, byte[] rawData = null)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                return;

            if(rawData == null)
                rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);

            TempPlayers.Clear();
            MyAPIGateway.Players.GetPlayers(TempPlayers);

            foreach(var p in TempPlayers)
            {
                if(p.SteamUserId == MyAPIGateway.Multiplayer.ServerId)
                    continue;

                if(p.SteamUserId == packet.SenderId)
                    continue;

                MyAPIGateway.Multiplayer.SendMessageTo(PacketId, rawData, p.SteamUserId);
            }

            TempPlayers.Clear();
        }

        public void SendToServer(PacketBase packet)
        {
            var bytes = MyAPIGateway.Utilities.SerializeToBinary(packet);
            MyAPIGateway.Multiplayer.SendMessageToServer(PacketId, bytes);
        }

        public void SendToPlayer(PacketBase packet, ulong steamId)
        {
            var bytes = MyAPIGateway.Utilities.SerializeToBinary(packet);
            MyAPIGateway.Multiplayer.SendMessageTo(PacketId, bytes, steamId);
        }
    }
}
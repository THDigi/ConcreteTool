using System;
using VRageMath;

namespace Digi
{
    public static class PacketHandler
    {
        public static void AddToArray(bool value, ref int index, ref byte[] bytes)
        {
            bytes[index] = (byte)(value ? 1 : 0);
            index += 1;
        }
        
        public static void AddToArray(byte value, ref int index, ref byte[] bytes)
        {
            bytes[index] = value;
            index += 1;
        }
        
        public static void AddToArray(int value, ref int index, ref byte[] bytes)
        {
            var data = BitConverter.GetBytes(value);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
        }
        
        public static void AddToArray(long value, ref int index, ref byte[] bytes)
        {
            var data = BitConverter.GetBytes(value);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
        }
        
        public static void AddToArray(ulong value, ref int index, ref byte[] bytes)
        {
            var data = BitConverter.GetBytes(value);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
        }
        
        public static void AddToArray(float value, ref int index, ref byte[] bytes)
        {
            var data = BitConverter.GetBytes(value);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
        }
        
        public static void AddToArray(double value, ref int index, ref byte[] bytes)
        {
            var data = BitConverter.GetBytes(value);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
        }
        
        public static void AddToArray(Vector3 value, ref int index, ref byte[] bytes)
        {
            var data = BitConverter.GetBytes(value.X);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
            
            data = BitConverter.GetBytes(value.Y);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
            
            data = BitConverter.GetBytes(value.Z);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
        }
        
        public static void AddToArray(Vector3D value, ref int index, ref byte[] bytes)
        {
            var data = BitConverter.GetBytes(value.X);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
            
            data = BitConverter.GetBytes(value.Y);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
            
            data = BitConverter.GetBytes(value.Z);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;
        }
    }
}
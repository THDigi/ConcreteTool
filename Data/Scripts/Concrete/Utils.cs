using System;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;

namespace Digi.ConcreteTool
{
    public static class Utils
    {
        public static MyFixedPoint GetAmmoUsage(VoxelActionEnum type, float scale)
        {
            if(type == VoxelActionEnum.ADD_VOXEL)
                return (MyFixedPoint)(ConcreteToolMod.CONCRETE_PLACE_USE * scale);

            if(type == VoxelActionEnum.PAINT_VOXEL)
                return (MyFixedPoint)(ConcreteToolMod.CONCRETE_PAINT_USE * scale);

            return 0;
        }

        public static bool IsFaceVisible(Vector3D origin, Vector3 normal)
        {
            var dir = (origin - MyTransparentGeometry.Camera.Translation);
            return Vector3D.Dot(normal, dir) < 0;
        }

        public static Vector3I RotateInput(bool newPressed)
        {
            Vector3I result;

            if(newPressed)
            {
                result.X = (MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE) ? -1 : (MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE) ? 1 : 0));
                result.Y = (MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE) ? -1 : (MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE) ? 1 : 0));
                result.Z = (MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE) ? -1 : (MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE) ? 1 : 0));
            }
            else
            {
                result.X = (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE) ? -1 : (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE) ? 1 : 0));
                result.Y = (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE) ? -1 : (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE) ? 1 : 0));
                result.Z = (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE) ? -1 : (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE) ? 1 : 0));
            }

            return result;
        }

        public static void PlayLocalSound(string soundName, float volume = 0.3f)
        {
            var emitter = new MyEntity3DSoundEmitter((MyEntity)MyAPIGateway.Session.ControlledObject.Entity);
            emitter.CustomVolume = volume;
            emitter.PlaySingleSound(new MySoundPair(soundName));
        }

        public static void PlaySoundAt(IMyEntity ent, string soundName, float volume = 0.3f)
        {
            var emitter = new MyEntity3DSoundEmitter((MyEntity)ent);
            emitter.CustomVolume = volume;
            emitter.PlaySingleSound(new MySoundPair(soundName));
        }

        public static void PreventConcreteToolVanillaFiring()
        {
            // make the concrete tool not be able to shoot normally, to avoid needing to add ammo and the stupid hardcoded screen shake
            var gunDef = MyDefinitionManager.Static.GetWeaponDefinition(new MyDefinitionId(typeof(MyObjectBuilder_WeaponDefinition), ConcreteToolMod.CONCRETE_WEAPON_ID));

            for(int i = 0; i < gunDef.WeaponAmmoDatas.Length; i++)
            {
                var ammoData = gunDef.WeaponAmmoDatas[i];

                if(ammoData == null)
                    continue;

                ammoData.ShootIntervalInMiliseconds = int.MaxValue;
            }
        }
    }
}

using System;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum; // HACK allows the use of BlendTypeEnum which is whitelisted but bypasses accessing MyBillboard which is not whitelisted

namespace Digi.ConcreteTool
{
    public static class Utils
    {
        public static MyFixedPoint GetAmmoUsage(VoxelActionEnum type, float scale)
        {
            switch(type)
            {
                case VoxelActionEnum.ADD_VOXEL: return (MyFixedPoint)Math.Ceiling(ConcreteToolMod.CONCRETE_PLACE_USE * (scale * scale * scale));
                case VoxelActionEnum.PAINT_VOXEL: return (MyFixedPoint)Math.Ceiling(ConcreteToolMod.CONCRETE_PAINT_USE * (scale * scale * scale));
                default: return 0;
            }
        }

        public static bool IsFaceVisible(Vector3D origin, Vector3 normal)
        {
            var dir = (origin - MyTransparentGeometry.Camera.Translation);
            return Vector3D.Dot(normal, dir) < 0;
        }

        public static void DrawHighlightBox(MatrixD matrix, float scale, float lineWidth, MyStringId material, Color colorFace, Color colorWire, BlendTypeEnum blendType)
        {
            // optimized box draw compared to MySimpleObjectDraw.DrawTransparentBox; also allows consistent edge thickness
            MyQuadD quad;
            Vector3D p;
            MatrixD m;
            var halfScale = (scale * 0.5f);
            float lineLength = scale;
            const float ROTATE_90_RAD = (90f / 180f * MathHelper.Pi); // 90deg in radians

            p = matrix.Translation + matrix.Forward * halfScale;
            if(IsFaceVisible(p, matrix.Forward))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref matrix);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: blendType);
            }

            p = matrix.Translation + matrix.Backward * halfScale;
            if(IsFaceVisible(p, matrix.Backward))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref matrix);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: blendType);
            }

            p = matrix.Translation + matrix.Left * halfScale;
            m = matrix * MatrixD.CreateFromAxisAngle(matrix.Up, ROTATE_90_RAD);
            if(IsFaceVisible(p, matrix.Left))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: blendType);
            }

            p = matrix.Translation + matrix.Right * halfScale;
            if(IsFaceVisible(p, matrix.Right))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: blendType);
            }

            m = matrix * MatrixD.CreateFromAxisAngle(matrix.Left, ROTATE_90_RAD);
            p = matrix.Translation + matrix.Up * halfScale;
            if(IsFaceVisible(p, matrix.Up))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: blendType);
            }

            p = matrix.Translation + matrix.Down * halfScale;
            if(IsFaceVisible(p, matrix.Down))
            {
                MyUtils.GenerateQuad(out quad, ref p, halfScale, halfScale, ref m);
                MyTransparentGeometry.AddQuad(material, ref quad, colorFace, ref p, blendType: blendType);
            }

            var upHalf = (matrix.Up * halfScale);
            var rightHalf = (matrix.Right * halfScale);
            var forwardHalf = (matrix.Forward * halfScale);

            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + upHalf + -rightHalf + matrix.Forward * halfScale, matrix.Backward, lineLength, lineWidth, blendType: blendType);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + upHalf + rightHalf + matrix.Forward * halfScale, matrix.Backward, lineLength, lineWidth, blendType: blendType);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + -upHalf + -rightHalf + matrix.Forward * halfScale, matrix.Backward, lineLength, lineWidth, blendType: blendType);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + -upHalf + rightHalf + matrix.Forward * halfScale, matrix.Backward, lineLength, lineWidth, blendType: blendType);

            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + forwardHalf + -rightHalf + matrix.Up * halfScale, matrix.Down, lineLength, lineWidth, blendType: blendType);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + forwardHalf + rightHalf + matrix.Up * halfScale, matrix.Down, lineLength, lineWidth, blendType: blendType);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + -forwardHalf + -rightHalf + matrix.Up * halfScale, matrix.Down, lineLength, lineWidth, blendType: blendType);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + -forwardHalf + rightHalf + matrix.Up * halfScale, matrix.Down, lineLength, lineWidth, blendType: blendType);

            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + forwardHalf + -upHalf + matrix.Right * halfScale, matrix.Left, lineLength, lineWidth, blendType: blendType);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + forwardHalf + upHalf + matrix.Right * halfScale, matrix.Left, lineLength, lineWidth, blendType: blendType);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + -forwardHalf + -upHalf + matrix.Right * halfScale, matrix.Left, lineLength, lineWidth, blendType: blendType);
            MyTransparentGeometry.AddLineBillboard(material, colorWire, matrix.Translation + -forwardHalf + upHalf + matrix.Right * halfScale, matrix.Left, lineLength, lineWidth, blendType: blendType);
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

        public static void PlayLocalSound(MySoundPair soundPair, float volume = 0.3f, uint timeout = 0)
        {
            var mod = ConcreteToolMod.Instance;

            if(!mod.init)
                return;

            if(mod.isThisDedicated)
                throw new Exception("Sounds shouldn't play on DS side!");

            if(timeout > 0)
            {
                var tick = mod.tick;

                if(mod.hudSoundTimeout > tick)
                    return;

                mod.hudSoundTimeout = tick + timeout;
            }

            var emitter = mod.hudSoundEmitter;

            if(emitter == null)
                mod.hudSoundEmitter = emitter = new MyEntity3DSoundEmitter(null);

            emitter.SetPosition(MyAPIGateway.Session.Camera.WorldMatrix.Translation);
            emitter.CustomVolume = volume;
            emitter.PlaySound(soundPair, stopPrevious: false, alwaysHearOnRealistic: true, force2D: true);
        }

        public static void PlaySoundAt(IMyEntity ent, MySoundPair soundPair, float volume = 0.3f)
        {
            var emitter = new MyEntity3DSoundEmitter((MyEntity)ent);
            emitter.CustomVolume = volume;
            emitter.PlaySingleSound(soundPair);
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

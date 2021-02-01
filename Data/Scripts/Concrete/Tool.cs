using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Digi.ConcreteTool
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AutomaticRifle), useEntityUpdate: false, entityBuilderSubTypeNames: ConcreteToolMod.CONCRETE_TOOL)]
    public class Tool : MyGameLogicComponent
    {
        private ConcreteToolMod Mod => ConcreteToolMod.Instance;

        private bool first = true;
        private IMyGunBaseUser tool;
        private IMyCharacter owner;
        private MyEntitySubpart subpart = null;
        private float currentAngle = 0;
        private float torque = 0;
        private MatrixD prevMatrixInv;
        private Vector3D prevPos;
        private Vector3 prevVelAtPos;
        private Vector3 gravityCache;
        private int gravitySkip = GRAVITY_CHECK_TICKS;

        private const int GRAVITY_CHECK_TICKS = 60; // gravity every this ticks
        private const double SUBPART_DISTANCE_SQ = 50 * 50; // max view distance squared that subpart is simulated at
        private const float SUBPART_MIN_ANGLE = 0;
        private const float SUBPART_MAX_ANGLE = 70;
        private const string SUBPART_NAME = "Handle";

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME; // needs to be each tick to wait for mod to initialize
        }

        private void InitUpdate()
        {
            tool = (IMyGunBaseUser)Entity;
            owner = tool.Owner as IMyCharacter;

            if(Mod == null || owner == null || owner.Physics == null)
                return;

            first = false; // don't move this up because it needs to repeat until mod and character are available for a valid check

            // checking if local player is holding it
            if(owner != null && MyAPIGateway.Session.Player != null && tool.OwnerId == MyAPIGateway.Session.Player.IdentityId)
            {
                Mod.EquipTool((IMyAutomaticRifleGun)Entity);
            }

            if(!Entity.TryGetSubpart(SUBPART_NAME, out subpart))
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            prevMatrixInv = Entity.WorldMatrix;
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if(first)
                {
                    InitUpdate();
                    return;
                }

                SimulateDanglingSubpart();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void SimulateDanglingSubpart()
        {
            if(subpart == null)
                return;

            if(!HandleSubpartVisibility())
                return;

            const float FRICTION = 0.9f;
            const float BOUNCE = -0.6f;

            var accelAtPos = GetAccelAtPos();
            var dot = accelAtPos.Dot(Entity.WorldMatrix.Forward); // how much acceleration is in the tool's forward axis
            torque += dot;
            torque *= FRICTION;
            currentAngle += torque;

            // physical limits of the rotation
            if(currentAngle < 0)
            {
                currentAngle = SUBPART_MIN_ANGLE;
                torque *= BOUNCE;
            }
            else if(currentAngle > SUBPART_MAX_ANGLE)
            {
                currentAngle = SUBPART_MAX_ANGLE;
                torque *= BOUNCE;
            }

            var m = subpart.PositionComp.LocalMatrixRef;
            var lm = Matrix.CreateFromAxisAngle(m.Up, MathHelper.ToRadians(currentAngle));
            lm.Translation = m.Translation;
            subpart.PositionComp.SetLocalMatrix(ref lm);
        }

        private Vector3 GetAccelAtPos()
        {
            const float MULTIPLIER = 100f; // so it feelsTM right

            var subpartPos = subpart.WorldMatrix.Translation;
            var toolWM = Entity.WorldMatrix;
            var deltaMatrix = toolWM * prevMatrixInv;

            Vector3 toolAngVel = new Vector3(
                Math.Asin(Vector3D.Dot(deltaMatrix.Up, Vector3D.Backward)),
                Math.Asin(Vector3D.Dot(deltaMatrix.Backward, Vector3D.Right)),
                Math.Asin(Vector3D.Dot(deltaMatrix.Right, Vector3D.Up))
            );
            toolAngVel = Vector3.TransformNormal(toolAngVel, toolWM) * MULTIPLIER;

            Vector3 toolVel = (subpartPos - prevPos) * MULTIPLIER;
            Vector3 velAtPos = toolVel + toolAngVel.Cross(subpartPos - owner.WorldAABB.Center);
            Vector3 accelAtPos = (velAtPos - prevVelAtPos);

            Vector3 gravity = GetGravity(subpartPos);

            UpdatePrevData(velAtPos);

            accelAtPos -= (gravity * 0.1f);

            return accelAtPos;
        }

        private void UpdatePrevData(Vector3 velAtPos)
        {
            prevVelAtPos = velAtPos;
            prevMatrixInv = Entity.WorldMatrixInvScaled;
            prevPos = subpart.WorldMatrix.Translation;
        }

        /// <summary>
        /// Checks and hides subpart according to <see cref="SUBPART_DISTANCE_SQ"/> and returns true if it's visible.
        /// Also calls <see cref="UpdatePrevData()"/> to avoid any super-velocities.
        /// </summary>
        private bool HandleSubpartVisibility()
        {
            if(Vector3D.DistanceSquared(owner.WorldAABB.Center, MyAPIGateway.Session.Camera.WorldMatrix.Translation) > SUBPART_DISTANCE_SQ)
            {
                if(subpart.Render.Visible)
                    subpart.Render.Visible = false;

                return false;
            }

            if(!subpart.Render.Visible)
            {
                subpart.Render.Visible = true;
                UpdatePrevData(Vector3.Zero);
            }

            return true;
        }

        /// <summary>
        /// Returns both natural and artificial gravity vector
        /// </summary>
        private Vector3 GetGravity(Vector3D position)
        {
            if(++gravitySkip > GRAVITY_CHECK_TICKS)
            {
                gravitySkip = 0;

                float naturalGravityMultiplier;
                gravityCache = MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out naturalGravityMultiplier)
                             + MyAPIGateway.Physics.CalculateArtificialGravityAt(position, naturalGravityMultiplier);
            }

            return gravityCache;
        }
    }
}
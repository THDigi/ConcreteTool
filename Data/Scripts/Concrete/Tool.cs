using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Digi.Concrete
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AutomaticRifle), useEntityUpdate: false, entityBuilderSubTypeNames: Concrete.CONCRETE_TOOL)]
    public class Tool : MyGameLogicComponent
    {
        private bool first = true;
        private MyEntitySubpart subpart = null;
        private float currentAngle = 0;
        private float torque = 0;
        private Vector3 prevAngVel;
        private QuaternionD prevRotation;
        private int skipTicks = 99999;
        private MyPlanet planet = null;

        private const double VIEW_RANGE_SQ = 50 * 50;
        private const float MAX_ANGLE = 70;
        private const string SUBPART_NAME = "Handle";

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME; // needs to be each tick to wait for mod to initialize
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if(first)
                {
                    var mod = Concrete.instance;

                    if(mod == null)
                        return;

                    first = false; // don't move this up because it needs to repeat until mod and character are available for a valid check

                    if(MyAPIGateway.Session.Player != null) // it's null for DS and might be for other cases, we don't care for those cases
                    {
                        var gun = (IMyGunBaseUser)Entity;

                        // check if the local player is holding it
                        if(gun?.Owner != null && gun.OwnerId == MyAPIGateway.Session.Player.IdentityId)
                        {
                            mod.DrawTool((IMyAutomaticRifleGun)Entity);
                        }
                    }

                    if(!Entity.TryGetSubpart(SUBPART_NAME, out subpart))
                    {
                        NeedsUpdate = MyEntityUpdateEnum.NONE;
                        return;
                    }
                }

                if(subpart == null)
                    return;

                var tool = (IMyGunBaseUser)Entity;
                var character = tool.Owner as IMyCharacter;

                if(character == null || character.Physics == null)
                    return;

                var pos = character.PositionComp.WorldAABB.Center;

                if(Vector3D.DistanceSquared(pos, MyAPIGateway.Session.Camera.WorldMatrix.Translation) > VIEW_RANGE_SQ)
                    return;

                var angAccel = Vector3.Zero;

                if(character.CurrentMovementState == MyCharacterMovementEnum.Flying)
                {
                    var rotation = QuaternionD.CreateFromRotationMatrix(character.PositionComp.WorldMatrix);
                    var deltaRotation = rotation * QuaternionD.Inverse(prevRotation);
                    var angVel = Vector3.Zero;

                    // MATH - courtesy of Equinox
                    if(1 - deltaRotation.W * deltaRotation.W > 0)
                    {
                        angVel = 2 * Math.Acos(deltaRotation.W) * new Vector3D(deltaRotation.X, deltaRotation.Y, deltaRotation.Z) / Math.Sqrt(1 - (deltaRotation.W * deltaRotation.W)) / (1d / 60d);
                        angAccel = (prevAngVel - angVel) * 60f;
                    }

                    prevAngVel = angVel;
                    prevRotation = rotation;
                }
                else
                {
                    angAccel = character.Physics.AngularAcceleration;
                }

                var wm = subpart.WorldMatrix;
                var subpartPos = wm.Translation; // + wm.Forward * 0.1 + wm.Left * 0.05;
                var accel = character.Physics.LinearAcceleration + angAccel.Cross(subpartPos - pos);

                if(++skipTicks > 60)
                    planet = MyGamePruningStructure.GetClosestPlanet(pos);

                if(planet != null)
                {
                    if(planet.Closed)
                    {
                        planet = null;
                    }
                    else
                    {
                        var gravComp = planet.Components.Get<MyGravityProviderComponent>();

                        if(gravComp != null)
                            accel -= gravComp.GetWorldGravity(pos);
                    }
                }

                var dot = accel.Dot(Entity.WorldMatrix.Forward) / 20f; // how much acceleration is in the tool's forward axis
                torque += dot;
                torque *= 0.9f; // drag
                currentAngle += torque;

                // physical limits of the rotation
                if(currentAngle < 0)
                {
                    currentAngle = 0;
                    torque = Math.Abs(dot); // bounce
                }
                else if(currentAngle > MAX_ANGLE)
                {
                    currentAngle = MAX_ANGLE;
                    torque = -Math.Abs(dot); // bounce
                }

                var m = subpart.PositionComp.LocalMatrix;
                var rm = Matrix.CreateFromAxisAngle(m.Up, MathHelper.ToRadians(currentAngle));
                rm.Translation = m.Translation;
                subpart.PositionComp.LocalMatrix = rm;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}
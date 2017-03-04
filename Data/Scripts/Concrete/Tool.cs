using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace Digi.Concrete
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AutomaticRifle),
        useEntityUpdate: true,
        entityBuilderSubTypeNames: Concrete.CONCRETE_TOOL)]
    public class Tool : MyGameLogicComponent
    {
        private bool first = true;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if(!first)
                    return;

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

                Entity.Components.Remove<Tool>();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}

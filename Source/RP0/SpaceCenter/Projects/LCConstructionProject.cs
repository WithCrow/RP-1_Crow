using System;
using UnityEngine;

namespace RP0
{
    public class LCConstructionProject : ConstructionProject
    {
        [Persistent]
        public bool isModify = false;

        /// <summary>Reactivating a mothballed LC.</summary>
        [Persistent]
        public bool isReactivation = false;

        [Persistent]
        public Guid modId;

        [Persistent]
        public Guid lcID;

        [Persistent]
        public LCData lcData = new LCData();

        [Persistent]
        public int engineersToReadd = 0;


        public LCConstructionProject()
        {
        }

        public override void Load(ConfigNode node)
        {
            base.Load(node);

            if (SpaceCenterManagement.Instance.LoadedSaveVersion < SpaceCenterManagement.VERSION)
            {
                if (SpaceCenterManagement.Instance.LoadedSaveVersion < 8)
                {
                    var keys = new System.Collections.Generic.List<string>(lcData.resourcesHandled.Keys);
                    foreach (var k in keys)
                    {
                        lcData.resourcesHandled[k] = Math.Ceiling(lcData.resourcesHandled[k]);
                    }
                }
            }
        }

        protected override void ProcessComplete()
        {
            if (!SpaceCenterManagement.Instance.ErroredDuringOnLoad)
            {
                LaunchComplex lc = KSC.LaunchComplexes.Find(l => l.ID == lcID);
                if (isReactivation)
                {
                    // Pads were never made non-operational, so they come back with the LC. Engineers are not reassigned.
                    upgradeProcessed = true;
                    lc.CompleteReactivation();
                    try
                    {
                        SCMEvents.OnLCRecommissioned?.Fire(lc);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                    return;
                }

                lc.IsOperational = true;
                upgradeProcessed = true;
                if (isModify)
                    lc.Modify(lcData, modId);

                ReassignEngineers(lc);

                try
                {
                    SCMEvents.OnLCConstructionComplete?.Fire(this, lc);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        protected override void ProcessCancel()
        {
            int index = -1;
            LaunchComplex lc = null;
            for (int i = 0; i < KSC.LaunchComplexes.Count; ++i)
            {
                lc = KSC.LaunchComplexes[i];
                if (lc.ID == lcID)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                RP0Debug.LogError($"Can't find LC from LCC, LC ID {lcID}");
                return;
            }

            // Must be removed before LC.Delete() runs, since Delete() cancels all of the LC's constructions
            KSC.LCConstructions.Remove(this);

            if (isReactivation)
            {
                // Back to mothballed with the dormant clocks resumed. Progress/funds spent are not refunded (as other LC projects).
                lc.ResumeAfterReactivationCancel();
                KSC.RecalculateBuildRates(false);
                return;
            }

            if (isModify)
            {
                lc.IsOperational = true;
                lc.RecalculateBuildRates();
                lc.Stats.GetCostStats(out double padCost, out _, out _);
                padCost *= Database.SettingsSC.AdditionalPadCostMult;
                foreach (var pc in lc.PadConstructions)
                {
                    pc.SetBP(padCost, 0d);
                    pc.cost = padCost;
                }

                ReassignEngineers(lc);
            }
            else
            {
                KSC.LaunchComplexes[index].Delete();
            }

            try
            {
                SCMEvents.OnLCConstructionCancel?.Fire(this, lc);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            KSC.RecalculateBuildRates(false);
        }

        private void ReassignEngineers(LaunchComplex lc)
        {
            if (engineersToReadd == 0)
                return;

            int engToAssign = Math.Min(engineersToReadd, Math.Min(lc.MaxEngineers, lc.KSC.UnassignedEngineers));
            if (engToAssign > 0)
            {
                KCTUtilities.ChangeEngineers(lc, engToAssign);
            }
        }
    }
}

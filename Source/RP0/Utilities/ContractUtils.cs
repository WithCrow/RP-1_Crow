namespace RP0
{
    public static class ContractUtils
    {
        public static bool ContractIsRecord(ContractConfigurator.ConfiguredContract cc)
        {
            return cc.contractType.group.name == "Records" || cc.contractType.group.name == "HumanRecords";
        }

        public static bool ContractIsRace(ContractConfigurator.ConfiguredContract cc)
        {
            return cc.contractType.group.name == "RaceFirsts";
        }

        public static bool ContractIsSkoposMaintenance(ContractConfigurator.ConfiguredContract cc)
        {
            return cc.contractType.group.name == "CommApp" && cc.contractType.name.StartsWith("maintenance_");
        }
    }
}

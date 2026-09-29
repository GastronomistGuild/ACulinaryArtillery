namespace ACulinaryArtillery.Util
{
    class ACulinaryArtilleryConfig
    {
        public string OvenDTMod = "Modifies The Rate At Which The Oven Heats, Cools, and Bakes (1.25 for Vanilla)";
        public float BEExpandedOvenDTMod;
        public string FuelBurnRateMod = "Multiplier Applied To BurnRate of Fuels Used In Oven (1.25 for Vanilla)";
        public float BEExpandedOvenFuelBurnRateMod;
        public string OvenBakeTimeMod = "Additional Direct Modifier To Bake Rate (Does Not Affect Heating/Cooling Time (1.2 for Vanilla)";
        public float BEExpandedOvenBakeTimeMod;
        public string SpileCountPerTree = "Allow a certain number of spiles per tree. Set to 0 to disable the restriction entirely.";
        public int BlockSpileCountPerTree;

        public ACulinaryArtilleryConfig()
        {

        }

        public static ACulinaryArtilleryConfig Current { get; set; } = GetDefault();

        public static ACulinaryArtilleryConfig GetDefault()
        {
            ACulinaryArtilleryConfig defaultConfig = new();

            defaultConfig.OvenDTMod.ToString();
            defaultConfig.BEExpandedOvenDTMod = 1.25f;
            defaultConfig.FuelBurnRateMod.ToString();
            defaultConfig.BEExpandedOvenFuelBurnRateMod = 1.25f;
            defaultConfig.OvenBakeTimeMod.ToString();
            defaultConfig.BEExpandedOvenBakeTimeMod = 1.2f;
            defaultConfig.SpileCountPerTree.ToString();
            defaultConfig.BlockSpileCountPerTree = 1;
            return defaultConfig;
        }
    }
}

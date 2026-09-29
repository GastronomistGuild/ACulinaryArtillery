namespace ACulinaryArtillery.Util
{
    class ACulinaryArtilleryConfig
    {
        public string OvenDTMod = "Modifies The Rate At Which The Oven Heats, Cools, and Bakes (1.25 for Vanilla)";
        public float BEExpandedOvenDTMod = 1.25f;
        public string FuelBurnRateMod = "Multiplier Applied To BurnRate of Fuels Used In Oven (1.25 for Vanilla)";
        public float BEExpandedOvenFuelBurnRateMod = 1.25f;
        public string OvenBakeTimeMod = "Additional Direct Modifier To Bake Rate (Does Not Affect Heating/Cooling Time (1.2 for Vanilla)";
        public float BEExpandedOvenBakeTimeMod = 1.2f;
        public string SpileCountPerTree = "Allow a certain number of spiles per tree. Set to 0 to disable the restriction entirely.";
        public int BlockSpileCountPerTree = 1;


        public static ACulinaryArtilleryConfig Current { get; set; } = GetDefault();

        public static ACulinaryArtilleryConfig GetDefault()
        {
            return new();
        }
    }
}

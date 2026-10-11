namespace ACulinaryArtillery.Util
{
    class ACulinaryArtilleryConfig
    {
        public string OvenDTMod = "Modifies the rate at which the oven heats, cools, and bakes (1.25 for vanilla)";
        public float BEExpandedOvenDTMod = 1.25f;
        public string FuelBurnRateMod = "Multiplier applied to burn rate of fuels used in oven (1.25 for vanilla)";
        public float BEExpandedOvenFuelBurnRateMod = 1.25f;
        public string OvenBakeTimeMod = "Additional direct modifier to bake rate (does not affect heating/cooling time (1.2 for vanilla)";
        public float BEExpandedOvenBakeTimeMod = 1.2f;
        public string MealHealDuration = "The amount of time it takes for healing from eating a meal to complete. Default is 180 seconds, or 3 minutes.";
        public float MealHealDurationSec = 180;
        public string MealHealTickCount = "The number of ticks that occur while healing from eating a meal. Defaults to 18, i.e. one tick every 10 seconds with the default duration of 180 seconds.";
        public int MealHealTicks = 18;

        public static ACulinaryArtilleryConfig Current { get; set; } = GetDefault();

        public static ACulinaryArtilleryConfig GetDefault()
        {
            return new();
        }
    }
}

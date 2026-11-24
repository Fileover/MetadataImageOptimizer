using System;

namespace MetadataImageOptimizer.Models
{
    public class OptimizeQueueItem
    {
        public OptimizeQueueItem(
            Guid gameId
            , bool optimizeBackground
            , bool optimizeCover
            , bool optimizeIcon
            , bool optimizeAddons = true)
        {
            GameId = gameId;
            OptimizeBackground = optimizeBackground;
            OptimizeCover = optimizeCover;
            OptimizeIcon = optimizeIcon;
            OptimizeAddons = optimizeAddons;
        }

        public Guid GameId { get; set; }
        public bool OptimizeBackground { get; set; }
        public bool OptimizeCover { get; set; }
        public bool OptimizeIcon { get; set; }
        public bool OptimizeAddons { get; set; }
    }
}

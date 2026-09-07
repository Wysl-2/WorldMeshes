public enum TerrainRuntimeInvalidationScenario
{
    None,
    SingleTileEdit,
    MultiTileEdit,
    ModifierMove,
    ModifierDistantMove,
    ModifierScale,
    ModifierRotation,
    ModifierStrength,
    ModifierDelete,
    ModifierEnable,
    ModifierDisable,
    Undo,
    Redo,
    MultipleEdits,
    RepeatedRegionEdits,
    DisconnectedRegionEdits,
    SurfaceSettingsChange,
    CollisionSettingsChange,
    WorldLayoutChange,
    HeightTileSpanChange
}

public enum TerrainRuntimeInvalidationValidationOutcome
{
    NotRun,
    Passed,
    PassedWithWarnings,
    Failed,
    Blocked
}

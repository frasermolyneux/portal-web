using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public class MapRotationCfgParserTests
{
    [Fact]
    public void Parse_StandardRotation_ExtractsGameModeAndMaps()
    {
        // Arrange
        var cfg = """
            set sv_maprotation "gametype ftag map mp_backlot map mp_crash map mp_crossfire"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Equal("ftag", result[0].GameMode);
        Assert.Equal(3, result[0].MapNames.Count);
        Assert.Equal("mp_backlot", result[0].MapNames[0]);
        Assert.Equal("mp_crash", result[0].MapNames[1]);
        Assert.Equal("mp_crossfire", result[0].MapNames[2]);
        Assert.True(result[0].IsActive);
    }

    [Fact]
    public void Parse_CommentedRotation_DetectedAsInactive()
    {
        // Arrange
        var cfg = """
            //set sv_maprotation "gametype tdm map mp_strike map mp_vacant"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.False(result[0].IsActive);
        Assert.Equal("tdm", result[0].GameMode);
        Assert.Equal(2, result[0].MapNames.Count);
    }

    [Fact]
    public void Parse_MultipleRotations_AllDetected()
    {
        // Arrange
        var cfg = """
            //Rotation #1
            //set sv_maprotation "gametype ftag map mp_backlot map mp_crash"

            //Rotation #2
            set sv_maprotation "gametype ftag map mp_strike map mp_vacant map mp_bog"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.False(result[0].IsActive);
        Assert.True(result[1].IsActive);
        Assert.Equal(2, result[0].MapNames.Count);
        Assert.Equal(3, result[1].MapNames.Count);
    }

    [Fact]
    public void Parse_AacpFormat_ParsesSemicolonSeparatedMaps()
    {
        // Arrange
        var cfg = """
            set scr_aacp_maps_1 "mp_backlot;mp_crash;mp_crossfire;mp_strike"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Equal(4, result[0].MapNames.Count);
        Assert.Equal("mp_backlot", result[0].MapNames[0]);
        Assert.Equal("mp_strike", result[0].MapNames[3]);
        Assert.Equal("", result[0].GameMode);
        Assert.Equal("scr_aacp_maps_1", result[0].ConfigVariableName);
    }

    [Fact]
    public void Parse_PlayerBasedRotations_AllThreeDetected()
    {
        // Arrange
        var cfg = """
            set scr_small_rotation "gametype dm map mp_shipment map mp_killhouse"
            set scr_med_rotation "gametype dm map mp_backlot map mp_crash map mp_strike"
            set scr_large_rotation "gametype dm map mp_bog map mp_overgrown map mp_pipeline map mp_creek"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("scr_small_rotation", result[0].ConfigVariableName);
        Assert.Equal("scr_med_rotation", result[1].ConfigVariableName);
        Assert.Equal("scr_large_rotation", result[2].ConfigVariableName);
        Assert.Equal(2, result[0].MapNames.Count);
        Assert.Equal(3, result[1].MapNames.Count);
        Assert.Equal(4, result[2].MapNames.Count);
    }

    [Fact]
    public void Parse_MetadataExtraction_ExtractsRotationNameAuthorDate()
    {
        // Arrange
        var cfg = """
            //Rotation #17  25 maps   put in 04/05/2026 by Pengy
            set sv_maprotation "gametype ftag map mp_backlot map mp_crash"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Equal("Rotation #17", result[0].Title);
        Assert.Equal("Pengy", result[0].Author);
        Assert.Equal("04/05/2026", result[0].DateText);
    }

    [Fact]
    public void Parse_MetadataExtraction_HandlesVariousDateFormats()
    {
        // Arrange
        var cfg = """
            //Rotation 2	12 maps     put in 8/24/2023 Merlin
            set sv_maprotation "gametype tdm map mp_strike"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Equal("Rotation 2", result[0].Title);
        Assert.Equal("8/24/2023", result[0].DateText);
    }

    [Fact]
    public void Parse_NoMetadataComment_UsesFallbackTitle()
    {
        // Arrange
        var cfg = """
            set sv_maprotation "gametype war map mp_backlot"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Equal("sv_maprotation rotation", result[0].Title);
    }

    [Fact]
    public void Parse_ContinuationVariables_MergedIntoParent()
    {
        // Arrange
        var cfg = """
            set sv_maprotation "gametype ftag map mp_backlot map mp_crash"
            set sv_maprotation_1 "map mp_strike map mp_vacant"
            set sv_maprotation_2 "map mp_bog map mp_overgrown"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Equal(6, result[0].MapNames.Count);
        Assert.Equal("mp_backlot", result[0].MapNames[0]);
        Assert.Equal("mp_crash", result[0].MapNames[1]);
        Assert.Equal("mp_strike", result[0].MapNames[2]);
        Assert.Equal("mp_vacant", result[0].MapNames[3]);
        Assert.Equal("mp_bog", result[0].MapNames[4]);
        Assert.Equal("mp_overgrown", result[0].MapNames[5]);
    }

    [Fact]
    public void Parse_SectionDividers_DoNotBreakParsing()
    {
        // Arrange
        var cfg = """
            //******************************************************************************
            // MAP ROTATION SETTINGS
            //******************************************************************************

            //Rotation #1
            set sv_maprotation "gametype ftag map mp_backlot"

            //******************************************************************************
            // Bad Rotations
            //******************************************************************************
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Equal("Rotation #1", result[0].Title);
    }

    [Fact]
    public void Parse_NonRotationVariables_Ignored()
    {
        // Arrange
        var cfg = """
            set g_gametype "ftag"
            set scr_rotateifempty_enable "1"
            set sv_hostname "My Server"
            set sv_maprotation "gametype ftag map mp_backlot"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Equal("mp_backlot", result[0].MapNames[0]);
    }

    [Fact]
    public void Parse_EmptyConfig_ReturnsEmptyList()
    {
        // Arrange
        var cfg = """
            // Just some comments
            // No rotation lines here
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_EmptyRotationValue_ReturnsEmptyMapList()
    {
        // Arrange
        var cfg = """
            set sv_maprotation ""
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Empty(result[0].MapNames);
    }

    [Fact]
    public void Parse_CooperativeGametype_Detected()
    {
        // Arrange
        var cfg = """
            set sv_maprotation "gametype cooperative map mp_canal2 map mp_airfield map mp_castle"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Equal("cooperative", result[0].GameMode);
        Assert.Equal(3, result[0].MapNames.Count);
    }

    [Fact]
    public void Parse_RealWorldFtagConfig_ParsesCorrectly()
    {
        // Arrange — based on actual CoD4 FreezeTag Server 1 config
        var cfg = """
            set g_gametype "ftag"
            set scr_rotateifempty_enable "0"

            //Rotation #16      25   maps by sally rot3  put in 03/30/2026 by Pengy    
            //set sv_mapRotation "gametype ftag map mp_4hanoi map mp_beltot map mp_fav map mp_blackrock map mp_tigertown_v2"

            //Rotation #17    25 maps   put in 04/05/2026 by Pengy
            set sv_mapRotation "gametype ftag map mp_bo2mir map mp_lalustanya_v2 map mp_locality map mp_lapatrouille map mp_lpost"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Equal(2, result.Count);

        Assert.Equal("Rotation #16", result[0].Title);
        Assert.False(result[0].IsActive);
        Assert.Equal("sally", result[0].Author);
        Assert.Equal("03/30/2026", result[0].DateText);
        Assert.Equal(5, result[0].MapNames.Count);

        Assert.Equal("Rotation #17", result[1].Title);
        Assert.True(result[1].IsActive);
        Assert.Equal("Pengy", result[1].Author);
        Assert.Equal(5, result[1].MapNames.Count);
        Assert.Equal("mp_bo2mir", result[1].MapNames[0]);
    }

    [Fact]
    public void Parse_CaseInsensitiveVariableNames()
    {
        // Arrange
        var cfg = """
            set SV_MAPROTATION "gametype ftag map mp_backlot"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.Equal("ftag", result[0].GameMode);
    }

    [Fact]
    public void Parse_CommentedWithSpaces_StillDetected()
    {
        // Arrange
        var cfg = """
            //  set sv_maprotation "gametype tdm map mp_strike"
            """;

        // Act
        var result = MapRotationCfgParser.Parse(cfg);

        // Assert
        Assert.Single(result);
        Assert.False(result[0].IsActive);
    }
}

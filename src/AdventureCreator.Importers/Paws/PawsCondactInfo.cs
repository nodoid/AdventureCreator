namespace AdventureCreator.Importers.Paws;

/// <summary>The Spectrum PAWS condact set: opcode = index, with parameter count and whether it is a condition.</summary>
internal sealed record PawsCondactInfo(string Name, int Params, bool IsCondition)
{
    public static readonly PawsCondactInfo[] All =
    {
        new("AT", 1, true),        //   0
        new("NOTAT", 1, true),     //   1
        new("ATGT", 1, true),      //   2
        new("ATLT", 1, true),      //   3
        new("PRESENT", 1, true),   //   4
        new("ABSENT", 1, true),    //   5
        new("WORN", 1, true),      //   6
        new("NOTWORN", 1, true),   //   7
        new("CARRIED", 1, true),   //   8
        new("NOTCARR", 1, true),   //   9
        new("CHANCE", 1, true),    //  10
        new("ZERO", 1, true),      //  11
        new("NOTZERO", 1, true),   //  12
        new("EQ", 2, true),        //  13
        new("GT", 2, true),        //  14
        new("LT", 2, true),        //  15
        new("ADJECT1", 1, true),   //  16
        new("ADVERB", 1, true),    //  17
        new("INVEN", 0, false),    //  18
        new("DESC", 0, false),     //  19
        new("QUIT", 0, false),     //  20 (a condact: asks "Are you sure?")
        new("END", 0, false),      //  21
        new("DONE", 0, false),     //  22
        new("OK", 0, false),       //  23
        new("ANYKEY", 0, false),   //  24
        new("SAVE", 0, false),     //  25
        new("LOAD", 0, false),     //  26
        new("TURNS", 0, false),    //  27
        new("SCORE", 0, false),    //  28
        new("CLS", 0, false),      //  29
        new("DROPALL", 0, false),  //  30
        new("AUTOG", 0, false),    //  31
        new("AUTOD", 0, false),    //  32
        new("AUTOW", 0, false),    //  33
        new("AUTOR", 0, false),    //  34
        new("PAUSE", 1, false),    //  35
        new("TIMEOUT", 0, true),   //  36
        new("GOTO", 1, false),     //  37
        new("MESSAGE", 1, false),  //  38
        new("REMOVE", 1, false),   //  39
        new("GET", 1, false),      //  40
        new("DROP", 1, false),     //  41
        new("WEAR", 1, false),     //  42
        new("DESTROY", 1, false),  //  43
        new("CREATE", 1, false),   //  44
        new("SWAP", 2, false),     //  45
        new("PLACE", 2, false),    //  46
        new("SET", 1, false),      //  47
        new("CLEAR", 1, false),    //  48
        new("PLUS", 2, false),     //  49
        new("MINUS", 2, false),    //  50
        new("LET", 2, false),      //  51
        new("NEWLINE", 0, false),  //  52
        new("PRINT", 1, false),    //  53
        new("SYSMESS", 1, false),  //  54
        new("ISAT", 2, true),      //  55
        new("COPYOF", 2, false),   //  56
        new("COPYOO", 2, false),   //  57
        new("COPYFO", 2, false),   //  58
        new("COPYFF", 2, false),   //  59
        new("LISTOBJ", 0, false),  //  60
        new("EXTERN", 1, false),   //  61
        new("RAMSAVE", 0, false),  //  62
        new("RAMLOAD", 1, false),  //  63
        new("BEEP", 2, false),     //  64
        new("PAPER", 1, false),    //  65
        new("INK", 1, false),      //  66
        new("BORDER", 1, false),   //  67
        new("PREP", 1, true),      //  68
        new("NOUN2", 1, true),     //  69
        new("ADJECT2", 1, true),   //  70
        new("ADD", 2, false),      //  71
        new("SUB", 2, false),      //  72
        new("PARSE", 0, false),    //  73
        new("LISTAT", 1, false),   //  74
        new("PROCESS", 1, false),  //  75
        new("SAME", 2, true),      //  76
        new("MES", 1, false),      //  77
        new("CHARSET", 1, false),  //  78
        new("NOTEQ", 2, true),     //  79
        new("NOTSAME", 2, true),   //  80
        new("MODE", 2, false),     //  81
        new("LINE", 1, false),     //  82
        new("TIME", 2, false),     //  83
        new("PICTURE", 1, false),  //  84
        new("DOALL", 1, false),    //  85
        new("PROMPT", 1, false),   //  86
        new("GRAPHIC", 1, false),  //  87
        new("ISNOTAT", 2, true),   //  88
        new("WEIGH", 2, false),    //  89
        new("PUTIN", 2, false),    //  90
        new("TAKEOUT", 2, false),  //  91
        new("NEWTEXT", 0, false),  //  92
        new("ABILITY", 2, false),  //  93
        new("WEIGHT", 1, false),   //  94
        new("RANDOM", 1, false),   //  95
        new("INPUT", 1, false),    //  96
        new("SAVEAT", 0, false),   //  97
        new("BACKAT", 0, false),   //  98
        new("PRINTAT", 2, false),  //  99
        new("WHATO", 0, false),    // 100
        new("RESET", 1, false),    // 101
        new("PUTO", 1, false),     // 102
        new("NOTDONE", 0, false),  // 103
        new("AUTOP", 1, false),    // 104
        new("AUTOT", 1, false),    // 105
        new("MOVE", 1, false),     // 106
        new("PROTECT", 0, false),  // 107
    };

    public static int Opcode(string name) => Array.FindIndex(All, c => c.Name == name);
}

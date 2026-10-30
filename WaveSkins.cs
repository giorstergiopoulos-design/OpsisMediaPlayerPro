using System.Collections.Generic;

namespace MediaPlayerPro
{
    // Παλέτες των θεμάτων του Waveframe (renderer/styles.css), παραγόμενες από το αρχείο — η δεύτερη εμφάνιση «Wave».
    public partial class MainWindow
    {
        static readonly Skin[] WaveSkins =
        {
            new Skin { Name = "Wave", Bg = "#0B0B14", Bar = "#14141F", Fg = "#E7E7F2", Muted = "#9494AB", Accent = "#7C6CF0", Hover = "#2A2A40", Radius = 14 },
            new Skin { Name = "Wave Light", Bg = "#EEF0FB", Bar = "#FFFFFF", Fg = "#1B1B2C", Muted = "#6B6B85", Accent = "#6D5BF0", Hover = "#DCDCF0", Radius = 14 },
            new Skin { Name = "Wave Retro", Bg = "#000000", Bar = "#001200", Fg = "#39FF14", Muted = "#1F7A2B", Accent = "#39FF14", Hover = "#0F5C1F", Radius = 14 },
            new Skin { Name = "Wave Sunset", Bg = "#1A0F1F", Bar = "#2A1625", Fg = "#FDF0E8", Muted = "#C9A08F", Accent = "#FB7185", Hover = "#4A2A45", Radius = 14 },
            new Skin { Name = "Wave Mono", Bg = "#111114", Bar = "#1C1C20", Fg = "#E8E8EC", Muted = "#8A8A90", Accent = "#9CA3AF", Hover = "#333338", Radius = 14 },
            new Skin { Name = "Wave Ocean", Bg = "#071B26", Bar = "#0A2330", Fg = "#DFF3FA", Muted = "#7FA8B8", Accent = "#22B8CF", Hover = "#123244", Radius = 14 },
            new Skin { Name = "Wave Forest", Bg = "#0C1F14", Bar = "#102418", Fg = "#E3F5E9", Muted = "#86AB93", Accent = "#22C55E", Hover = "#1F4530", Radius = 14 },
            new Skin { Name = "Wave Rose", Bg = "#22101A", Bar = "#291420", Fg = "#FBE6F0", Muted = "#C98FA8", Accent = "#EC4899", Hover = "#4A2438", Radius = 14 },
            new Skin { Name = "Wave Amber", Bg = "#1C1305", Bar = "#261A0A", Fg = "#FDF1DC", Muted = "#C9A570", Accent = "#F59E0B", Hover = "#4A3410", Radius = 14 },
            new Skin { Name = "Wave Ice", Bg = "#EEF7FB", Bar = "#FFFFFF", Fg = "#123240", Muted = "#5C7F8C", Accent = "#0EA5E9", Hover = "#D3E8F0", Radius = 14 },
            new Skin { Name = "Wave Lavender", Bg = "#F3EFFB", Bar = "#FFFFFF", Fg = "#2E1F47", Muted = "#7D6A99", Accent = "#A855F7", Hover = "#E0D6F5", Radius = 14 },
            new Skin { Name = "Wave Crimson", Bg = "#1A0808", Bar = "#260E0E", Fg = "#FBE4E4", Muted = "#C98D8D", Accent = "#EF4444", Hover = "#4A1C1C", Radius = 14 },
            new Skin { Name = "Wave Emerald", Bg = "#04120E", Bar = "#082019", Fg = "#D9F5EC", Muted = "#6FAB9A", Accent = "#10B981", Hover = "#114337", Radius = 14 },
            new Skin { Name = "Wave Slate", Bg = "#10151C", Bar = "#161D26", Fg = "#E6EBF1", Muted = "#8B98A8", Accent = "#64748B", Hover = "#2A3441", Radius = 14 },
            new Skin { Name = "Wave Coral", Bg = "#FFF1EC", Bar = "#FFFFFF", Fg = "#4A231A", Muted = "#A3705F", Accent = "#FB7185", Hover = "#FFD9CC", Radius = 14 },
            new Skin { Name = "Wave Neon", Bg = "#06040F", Bar = "#0F0A1E", Fg = "#F0E6FF", Muted = "#9A86C9", Accent = "#D946EF", Hover = "#2C1A52", Radius = 14 },
            new Skin { Name = "Wave Sepia", Bg = "#F2E8D5", Bar = "#FFFAF0", Fg = "#3C2C17", Muted = "#8A7454", Accent = "#92400E", Hover = "#DDC9A3", Radius = 14 },
            new Skin { Name = "Wave Graphite", Bg = "#08090B", Bar = "#121316", Fg = "#E5E6E8", Muted = "#7C7F85", Accent = "#A1A1AA", Hover = "#26282D", Radius = 14 },
            new Skin { Name = "Wave Mint", Bg = "#EAFBF4", Bar = "#FFFFFF", Fg = "#08372A", Muted = "#4F8F7C", Accent = "#14B8A6", Hover = "#C9F0E0", Radius = 14 },
            new Skin { Name = "Wave Solar", Bg = "#1B1406", Bar = "#281E0A", Fg = "#FDF6D8", Muted = "#C9B06F", Accent = "#EAB308", Hover = "#4D3A12", Radius = 14 },
        };
    }
}

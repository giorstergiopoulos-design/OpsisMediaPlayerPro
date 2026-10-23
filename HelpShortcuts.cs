using System.Windows;
using System.Windows.Controls;

namespace MediaPlayerPro
{
    public partial class HelpWindow
    {
        void BuildHelp()
        {
            void Sec(string title)
            {
                var t = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 18, 0, 6) };
                t.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); HelpPanel.Children.Add(t);
            }
            void Row(string keys, string desc)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var wp = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
                foreach (var k in keys.Split('|'))
                {
                    if (k == "+" || k == "/")
                    {
                        var s = new TextBlock { Text = k, Margin = new Thickness(2, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                        s.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); wp.Children.Add(s); continue;
                    }
                    var cap = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(1, 1, 1, 2) };
                    cap.SetResourceReference(Border.BackgroundProperty, "HoverBrush"); cap.SetResourceReference(Border.BorderBrushProperty, "MutedBrush");
                    var t = new TextBlock { Text = k, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
                    t.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush"); cap.Child = t; wp.Children.Add(cap);
                }
                var d = new TextBlock { Text = desc, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                d.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
                Grid.SetColumn(d, 1); g.Children.Add(wp); g.Children.Add(d); HelpPanel.Children.Add(g);
            }
            void Note(string text)
            {
                var d = new TextBlock { Text = "•  " + text, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
                d.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush"); HelpPanel.Children.Add(d);
            }

            Sec("ΑΝΑΠΑΡΑΓΩΓΗ");
            Row("Space", "Αναπαραγωγή / Παύση");
            Row("E", "Καρέ-καρέ");
            Row("N", "Επόμενο αρχείο");
            Row("[|/|]", "Μείωση / αύξηση ταχύτητας");
            Row("L", "Επανάληψη A-B (πάτα δύο φορές)");
            Row("=|/|Backspace", "Κανονική ταχύτητα");
            Row("Home|/|End", "Προηγούμενο / επόμενο αρχείο");
            Row("T", "Χρόνος τρέχων / απομένων");
            Row("Ctrl|+|J", "Στατιστικά αναπαραγωγής");
            Sec("ΠΛΟΗΓΗΣΗ");
            Row("←|/|→", "Μετάβαση ∓5 δευτερόλεπτα");
            Row("Ctrl|+|←|/|→", "Μετάβαση ∓30 δευτερόλεπτα");
            Row("PgUp|/|PgDn", "Προηγούμενο / επόμενο κεφάλαιο");
            Row("Alt|+|←|/|→", "Μετάβαση ∓1 δευτερόλεπτο");
            Row("Shift|+|←|/|→", "Μετάβαση ∓15 δευτερόλεπτα");
            Row("Ctrl|+|Shift|+|←|/|→", "Μετάβαση ∓5 λεπτά");
            Row("Ctrl|+|G", "Μετάβαση σε συγκεκριμένο χρόνο");
            Row("Ctrl|+|B", "Προσθήκη σελιδοδείκτη");
            Sec("ΗΧΟΣ");
            Row("↑|/|↓", "Ένταση (έως 200%)");
            Row("M", "Σίγαση");
            Row("B", "Επόμενο κανάλι ήχου");
            Row("Ctrl|+|E", "Ενισχυτής ήχου (Equalizer και εφέ)");
            Sec("ΥΠΟΤΙΤΛΟΙ");
            Row("G", "Λήψη υποτίτλων");
            Row("V", "Επόμενο κανάλι υποτίτλων");
            Row("Y", "Συγχρονισμός στη γραμμή που ακούγεται");
            Row("Z|/|X", "Υπότιτλοι 100 ms νωρίτερα / αργότερα");
            Sec("ΠΑΡΑΘΥΡΟ ΚΑΙ ΕΜΦΑΝΙΣΗ");
            Row("F11|/|Enter", "Πλήρης οθόνη");
            Row("Esc", "Έξοδος από πλήρη οθόνη");
            Row("Ctrl|+|M", "Εμφάνιση / απόκρυψη μενού");
            Row("P", "Εμφάνιση / απόκρυψη πλευρικού πάνελ");
            Row("Ctrl|+|T", "Πάντα στο προσκήνιο");
            Row("Ctrl|+|Shift|+|T", "Επόμενο θέμα");
            Row("Ctrl|+|Shift|+|L", "Εναλλαγή σκούρη / φωτεινή / αυτόματη λειτουργία (και κουμπί στη γραμμή ελέγχου)");
            Row("S", "Στιγμιότυπο");
            Row("Ctrl|+|1|/|2|/|5", "Μέγεθος παραθύρου 100% / 200% / 50% του βίντεο");
            Row("Ctrl|+|Q", "Έξοδος");
            Sec("ΑΡΧΕΙΑ ΚΑΙ ΒΟΗΘΕΙΑ");
            Row("Ctrl|+|O", "Άνοιγμα αρχείων");
            Row("Ctrl|+|I", "Ιδιότητες πολυμέσου");
            Row("Del", "Αφαίρεση επιλεγμένου από τη λίστα");
            Row("F1", "Αυτή η βοήθεια");
            Sec("ΠΟΝΤΙΚΙ ΠΑΝΩ ΣΤΟ ΒΙΝΤΕΟ");
            Row("Κλικ", "Αναπαραγωγή / Παύση");
            Row("Διπλό κλικ", "Πλήρης οθόνη");
            Row("Ροδέλα", "Ένταση");
            Row("Ctrl|+|Ροδέλα", "Μετάβαση ∓5 δευτερόλεπτα");
            Row("Δεξί κλικ", "Μενού συντομεύσεων");
            Sec("ΣΥΧΝΑ ΠΡΟΒΛΗΜΑΤΑ");
            Note("Δεν βρέθηκαν υπότιτλοι: δοκίμασε άλλη γλώσσα στις ρυθμίσεις ή φόρτωσε αρχείο χειροκίνητα.");
            Note("Ο συγχρονισμός έδειξε χαμηλή βεβαιότητα: χρησιμοποίησε το Y.");
            Note("Αλλαγές στο μέγεθος/χρώμα υποτίτλων ισχύουν μετά από επανεκκίνηση.");
            Note("Η εφαρμογή θυμάται θέση αναπαραγωγής, θέμα και σελιδοδείκτες στον φάκελο %AppData%\\MediaPlayerPro.");
        }
    }
}

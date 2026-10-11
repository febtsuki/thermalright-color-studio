using System.Windows.Media;
using System.Windows;
using System.Windows.Data;

namespace ThermalrightColorStudio;
public static class StudioTheme
{
    sealed class ColorSlot : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(Color), typeof(ColorSlot));
        public Color Value { get => (Color)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    }
    static readonly Dictionary<SolidColorBrush, ColorSlot> slots = new();
    public static readonly SolidColorBrush Background = Make("#0B0F18"), Surface = Make("#131B29"), Sidebar = Make("#101722"),
        Text = Make("#EAF0FF"), Muted = Make("#91A0B8"), Accent = Make("#BCB4FF"), Button = Make("#243047"),
        Border = Make("#39465F"), Input = Make("#0C1320"), Table = Make("#182334"), TableAlternate = Make("#1D2B40");
    static SolidColorBrush Make(string hex)
    {
        var slot = new ColorSlot { Value = (Color)ColorConverter.ConvertFromString(hex) }; var brush = new SolidColorBrush();
        BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(ColorSlot.Value)) { Source = slot });
        slots.Add(brush, slot); return brush;
    }
    public static void Apply(bool light)
    {
        void Set(SolidColorBrush brush, string dark, string pale) => slots[brush].Value = (Color)ColorConverter.ConvertFromString(light ? pale : dark);
        Set(Background, "#0B0F18", "#F0F3F9"); Set(Surface, "#131B29", "#FFFFFF"); Set(Sidebar, "#101722", "#E7ECF5");
        Set(Text, "#EAF0FF", "#202C42"); Set(Muted, "#91A0B8", "#5B6B85"); Set(Accent, "#BCB4FF", "#6652B8");
        Set(Button, "#243047", "#E9EAF6"); Set(Border, "#39465F", "#C4CCDD"); Set(Input, "#0C1320", "#F7F9FC");
        Set(Table, "#182334", "#FFFFFF"); Set(TableAlternate, "#1D2B40", "#EDF1F8");
    }
}

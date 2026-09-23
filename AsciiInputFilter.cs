using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartVpn;

public static class AsciiInputFilter
{
    public static void ApplyTo(TextBox textBox)
    {
        if (textBox is null) return;

        textBox.GotKeyboardFocus += (_, _) => ForceEnglishKeyboard();

        textBox.PreviewTextInput += (_, e) =>
        {
            var converted = Normalize(e.Text);
            if (converted == e.Text) return;

            e.Handled = true;
            textBox.SelectedText = converted;
        };

        textBox.TextChanged += (_, _) =>
        {
            var converted = Normalize(textBox.Text);
            if (converted == textBox.Text) return;

            var selection = textBox.SelectionStart;
            textBox.Text = converted;
            textBox.SelectionStart = Math.Min(selection, converted.Length);
        };

        textBox.AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler((_, args) =>
        {
            if (!args.DataObject.GetDataPresent(typeof(string))) return;

            var pasted = args.DataObject.GetData(typeof(string)) as string;
            if (!string.IsNullOrEmpty(pasted))
                args.DataObject.SetData(typeof(string), Normalize(pasted));
        }), true);
    }

    public static void ApplyTo(PasswordBox passwordBox)
    {
        if (passwordBox is null) return;

        passwordBox.GotKeyboardFocus += (_, _) => ForceEnglishKeyboard();

        passwordBox.PasswordChanged += (_, _) =>
        {
            var converted = Normalize(passwordBox.Password);
            if (converted == passwordBox.Password) return;
            passwordBox.Password = converted;
        };

        passwordBox.AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler((_, args) =>
        {
            if (!args.DataObject.GetDataPresent(typeof(string))) return;

            var pasted = args.DataObject.GetData(typeof(string)) as string;
            if (!string.IsNullOrEmpty(pasted))
                args.DataObject.SetData(typeof(string), Normalize(pasted));
        }), true);
    }

    private static void ForceEnglishKeyboard()
    {
        try
        {
            var english = InputLanguageManager.Current.CurrentInputLanguage;
            foreach (var language in InputLanguageManager.Current.AvailableInputLanguages)
            {
                if (language is CultureInfo culture
                    && culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                {
                    english = culture;
                    break;
                }
            }
            if (english is not null)
                InputLanguageManager.Current.CurrentInputLanguage = english;
        }
        catch
        {
            // The requested keyboard layout may not be installed on the system.
        }
    }

    private static string Normalize(string value)
    {
        return new string(value.Select(MapCharacter).Where(ch => ch != '\0').ToArray());
    }

    private static char MapCharacter(char value) => value switch
    {
        'ض' => 'q', 'ص' => 'w', 'ث' => 'e', 'ق' => 'r', 'ف' => 't',
        'غ' => 'y', 'ع' => 'u', 'ه' => 'i', 'خ' => 'o', 'ح' => 'p',
        'ج' => '[', 'چ' => ']', 'ش' => 'a', 'س' => 's', 'ی' or 'ي' => 'd',
        'ب' => 'f', 'ل' => 'g', 'ا' => 'h', 'ت' => 'j', 'ن' => 'k',
        'م' => 'l', 'ک' or 'ك' => ';', 'گ' => '\'', 'ظ' => 'z',
        'ط' => 'x', 'ز' => 'c', 'ر' => 'v', 'ذ' => 'b', 'د' => 'n',
        'پ' => 'm', 'و' => ',', 'ژ' => 'C', 'ئ' => 'M',
        '۰' => '0', '۱' => '1', '۲' => '2', '۳' => '3', '۴' => '4',
        '۵' => '5', '۶' => '6', '۷' => '7', '۸' => '8', '۹' => '9',
        _ => value <= 127 ? value : '\0',
    };
}

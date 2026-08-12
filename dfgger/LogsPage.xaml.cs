using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace dfgger;

public partial class LogsPage : ContentPage
{
    public LogsPage()
    {
        InitializeComponent();

        // Aqui conectamos o LogList direto na lista inteligente do SistemaService.
        // Como ela é um ObservableCollection, a tela atualizará sozinha!
        LogList.ItemsSource = SistemaService.ListaDeLogs;
    }
}

// Classe que define como o log aparece na tela
public class EventoLog
{
    public string Titulo { get; set; }
    public string Horario { get; set; }
    public Color StatusColor { get; set; }
}
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace dfgger
{
    public partial class LogsPage : ContentPage
    {
        public LogsPage()
        {
            InitializeComponent();
            LogList.ItemsSource = SistemaService.ListaDeLogs;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await SistemaService.CarregarLogsDoFirebaseAsync();
        }
    }

    // Classe necessária para o SistemaService reconhecer os logs
    public class EventoLog
    {
        public string Titulo { get; set; } = string.Empty;
        public string Horario { get; set; } = string.Empty;
        public Color StatusColor { get; set; } = Colors.Gray;
    }
}
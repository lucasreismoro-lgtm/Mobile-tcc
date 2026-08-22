using System; // Importa tipos básicos do sistema (ex: EventArgs)
using Microsoft.Maui.Controls; // Importa os componentes e páginas da interface do MAUI

namespace dfgger // Declaração do namespace do projeto
{
    public partial class LogsPage : ContentPage // Define a página do histórico de logs do aplicativo
    {
        public LogsPage() // Construtor da página
        {
            InitializeComponent(); // Inicializa e carrega os componentes visuais declarados no XAML
        }

        // Método do ciclo de vida chamado automaticamente toda vez que a tela é exibida ao usuário
        protected override async void OnAppearing()
        {
            base.OnAppearing(); // Executa o comportamento padrão da classe base

            // Vincula a coleção observável de logs à lista visual da tela (LogList) para atualizar a UI em tempo real
            LogList.ItemsSource = SistemaService.ListaDeLogs;

            // Faz a chamada assíncrona para buscar e atualizar os registros mais recentes do Firebase Firestore
            await SistemaService.CarregarLogsDoFirebaseAsync();
        }

        // Evento disparado ao clicar no botão de voltar da interface
        private async void OnVoltarClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
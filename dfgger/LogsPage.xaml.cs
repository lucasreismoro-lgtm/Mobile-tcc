using System;
using Microsoft.Maui.Controls;

namespace dfgger
{
    public partial class LogsPage : ContentPage
    {
        public LogsPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Atribui a lista de logs diretamente ao CollectionView x:Name="LogList"
            LogList.ItemsSource = SistemaService.ListaDeLogs;

            // Busca os dados atualizados do Firebase
            await SistemaService.CarregarLogsDoFirebaseAsync();
        }

        private async void OnVoltarClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
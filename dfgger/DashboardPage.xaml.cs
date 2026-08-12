using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Google.Cloud.Firestore;

namespace dfgger
{
    public partial class DashboardPage : ContentPage
    {
        private IDispatcherTimer? _timer;
        private bool _isUpdatingProgrammatically = false;

        public DashboardPage(string nome, string cargo)
        {
            InitializeComponent();

            if (LblNomeUsuario != null)
                LblNomeUsuario.Text = string.IsNullOrWhiteSpace(nome) ? "Usuário" : nome;

            if (LblCargoUsuario != null)
            {
                LblCargoUsuario.Text = string.IsNullOrWhiteSpace(cargo)
                    ? "DONO DA CASA"
                    : cargo.Replace("_", " ").ToUpper();
            }

            Preferences.Set("CargoUsuario", cargo ?? "dono");

            ConfigurarTimer();
        }

        public DashboardPage() : this("Usuário", "dono")
        {
        }

        private void ConfigurarTimer()
        {
            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(5);
            _timer.Tick += (s, e) => VerificarSistemaAutomaticamente();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            _timer?.Start();
            AplicarPermissoesPorCargo();
            await CarregarEstadoSensoresDoFirebase();
            VerificarSistemaAutomaticamente();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // Para o timer quando o usuário sai da tela para economizar recursos e evitar memory leak
            _timer?.Stop();
        }

        private void AplicarPermissoesPorCargo()
        {
            string cargoUsuario = Preferences.Get("CargoUsuario", "dono").ToLower();
            bool ePermitido = cargoUsuario.Contains("dono") || cargoUsuario.Contains("admin") || cargoUsuario != "morador";

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (SwitchGeral != null) SwitchGeral.IsEnabled = ePermitido;
                if (SwitchPresenca != null) SwitchPresenca.IsEnabled = ePermitido;
                if (SwitchCalor != null) SwitchCalor.IsEnabled = ePermitido;
                if (SwitchAlarme != null) SwitchAlarme.IsEnabled = ePermitido;
            });
        }

        private async Task CarregarEstadoSensoresDoFirebase()
        {
            try
            {
                string cpfDono = Preferences.Get("CpfDonoCasa", string.Empty);
                if (string.IsNullOrEmpty(cpfDono)) return;

                // Garante a inicialização da conexão com Firebase
                if (SistemaService.FirestoreDb == null)
                {
                    await SistemaService.InicializarFirebase();
                    if (SistemaService.FirestoreDb == null) return;
                }

                _isUpdatingProgrammatically = true;

                DocumentReference docRef = SistemaService.FirestoreDb
                    .Collection("Usuarios")
                    .Document(cpfDono)
                    .Collection("Sensores")
                    .Document("estado");

                DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();

                if (snapshot.Exists)
                {
                    snapshot.TryGetValue("presencaAtivo", out bool presenca);
                    snapshot.TryGetValue("calorAtivo", out bool calor);
                    snapshot.TryGetValue("alarmeAtivo", out bool alarme);

                    SistemaService.PresencaAtivo = presenca;
                    SistemaService.CalorAtivo = calor;
                    SistemaService.AlarmeAtivo = alarme;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (SwitchPresenca != null) SwitchPresenca.IsToggled = presenca;
                        if (SwitchCalor != null) SwitchCalor.IsToggled = calor;
                        if (SwitchAlarme != null) SwitchAlarme.IsToggled = alarme;
                        if (SwitchGeral != null) SwitchGeral.IsToggled = presenca && calor && alarme;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao carregar sensores: {ex.Message}");
            }
            finally
            {
                _isUpdatingProgrammatically = false;
            }
        }

        private async Task SalvarEstadoSensoresNoFirebase()
        {
            try
            {
                string cpfDono = Preferences.Get("CpfDonoCasa", string.Empty);
                if (string.IsNullOrEmpty(cpfDono)) return;

                if (SistemaService.FirestoreDb == null)
                {
                    await SistemaService.InicializarFirebase();
                    if (SistemaService.FirestoreDb == null) return;
                }

                DocumentReference docRef = SistemaService.FirestoreDb
                    .Collection("Usuarios")
                    .Document(cpfDono)
                    .Collection("Sensores")
                    .Document("estado");

                Dictionary<string, object> dados = new Dictionary<string, object>
                {
                    { "presencaAtivo", SistemaService.PresencaAtivo },
                    { "calorAtivo", SistemaService.CalorAtivo },
                    { "alarmeAtivo", SistemaService.AlarmeAtivo }
                };

                await docRef.SetAsync(dados, SetOptions.MergeAll);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao salvar sensores: {ex.Message}");
            }
        }

        private void VerificarSistemaAutomaticamente()
        {
            try
            {
                bool temInternet = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
                bool firebaseOnline = SistemaService.IsFirebaseConectado;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (StatusIndicator == null || StatusLabel == null) return;

                    if (temInternet && firebaseOnline)
                    {
                        StatusIndicator.Fill = Colors.Green;
                        StatusLabel.Text = "Sistema Online";
                    }
                    else if (temInternet && !firebaseOnline)
                    {
                        StatusIndicator.Fill = Colors.Yellow;
                        StatusLabel.Text = "Atenção: Firebase Offline";
                    }
                    else
                    {
                        StatusIndicator.Fill = Colors.Red;
                        StatusLabel.Text = "Sistema Offline (Sem Rede)";
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao verificar status do sistema: {ex.Message}");
            }
        }

        private async void OnGeralToggled(object sender, ToggledEventArgs e)
        {
            if (_isUpdatingProgrammatically) return;

            try
            {
                bool estado = e.Value;

                _isUpdatingProgrammatically = true;
                if (SwitchPresenca != null) SwitchPresenca.IsToggled = estado;
                if (SwitchCalor != null) SwitchCalor.IsToggled = estado;
                if (SwitchAlarme != null) SwitchAlarme.IsToggled = estado;
                _isUpdatingProgrammatically = false;

                SistemaService.PresencaAtivo = estado;
                SistemaService.CalorAtivo = estado;
                SistemaService.AlarmeAtivo = estado;

                await SalvarEstadoSensoresNoFirebase();
                SistemaService.AdicionarLog("Sistema Geral", estado);
            }
            catch (Exception ex)
            {
                _isUpdatingProgrammatically = false;
                System.Diagnostics.Debug.WriteLine($"Erro ao alterar controle geral: {ex.Message}");
            }
        }

        private async void OnSensorToggled(object sender, ToggledEventArgs e)
        {
            if (_isUpdatingProgrammatically) return;

            try
            {
                if (sender is Switch switchOriginal)
                {
                    if (switchOriginal == SwitchPresenca)
                    {
                        SistemaService.PresencaAtivo = e.Value;
                        SistemaService.AdicionarLog("Sensor de Presença", e.Value);
                    }
                    else if (switchOriginal == SwitchCalor)
                    {
                        SistemaService.CalorAtivo = e.Value;
                        SistemaService.AdicionarLog("Sensor de Calor", e.Value);
                    }
                    else if (switchOriginal == SwitchAlarme)
                    {
                        SistemaService.AlarmeAtivo = e.Value;
                        SistemaService.AdicionarLog("Alarme Sonoro", e.Value);
                    }

                    await SalvarEstadoSensoresNoFirebase();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao alterar sensor individual: {ex.Message}");
            }
        }

        private async void OnVerLogsClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new LogsPage());
        }

        private async void OnSimularSensorClicked(object sender, EventArgs e)
        {
            SistemaService.RegistrarEventoExterno("Sensor de Presença", "MOVIMENTO DETECTADO!");
            await DisplayAlert("Alerta!", "Movimento detectado pelo sensor", "OK");
        }

        private async void OnVerMembrosClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new MembrosFamiliaPage());
        }
    }
}
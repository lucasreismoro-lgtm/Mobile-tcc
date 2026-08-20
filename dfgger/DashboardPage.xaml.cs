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

        // Instância do serviço de escuta da coleção de Eventos
        private readonly SensorAlertService _alertService = new SensorAlertService();

        // Variável de controle para ignorar o snapshot inicial da Dashboard
        private bool _primeiraCargaSensores = true;

        // Ouvinte em tempo real para mudanças de estado dos switches no Firestore
        private FirestoreChangeListener? _listenerSensores;

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

            // Reseta a trava ao entrar na página
            _primeiraCargaSensores = true;
            IniciarEscutaSensoresEmTempoReal();

            VerificarSistemaAutomaticamente();

            // Inicia a escuta em tempo real filtrada do SensorAlertService
            string cpfDono = Preferences.Get("CpfDonoCasa", string.Empty);
            if (!string.IsNullOrEmpty(cpfDono) && SistemaService.FirestoreDb != null)
            {
                _alertService.IniciarEscutaEventos(cpfDono, SistemaService.FirestoreDb);
            }
        }

        protected override async void OnDisappearing()
        {
            base.OnDisappearing();
            _timer?.Stop();

            // Interrompe as escutas ao sair da página para evitar vazamentos e alertas em lote
            _alertService.PararEscuta();

            if (_listenerSensores != null)
            {
                await _listenerSensores.StopAsync();
                _listenerSensores = null;
            }
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
                System.Diagnostics.Debug.WriteLine($"[ERRO CARREGAR SENSORES] {ex.Message}");
            }
            finally
            {
                _isUpdatingProgrammatically = false;
            }
        }

        private void IniciarEscutaSensoresEmTempoReal()
        {
            try
            {
                string cpfDono = Preferences.Get("CpfDonoCasa", string.Empty);
                if (string.IsNullOrEmpty(cpfDono) || SistemaService.FirestoreDb == null) return;

                DocumentReference docRef = SistemaService.FirestoreDb
                    .Collection("Usuarios")
                    .Document(cpfDono)
                    .Collection("Sensores")
                    .Document("estado");

                _listenerSensores = docRef.Listen(snapshot =>
                {
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
                            _isUpdatingProgrammatically = true;
                            if (SwitchPresenca != null) SwitchPresenca.IsToggled = presenca;
                            if (SwitchCalor != null) SwitchCalor.IsToggled = calor;
                            if (SwitchAlarme != null) SwitchAlarme.IsToggled = alarme;
                            if (SwitchGeral != null) SwitchGeral.IsToggled = presenca && calor && alarme;
                            _isUpdatingProgrammatically = false;
                        });

                        // Trava a primeira execução do listener para não re-disparar pop-ups ao carregar a página
                        if (_primeiraCargaSensores)
                        {
                            _primeiraCargaSensores = false;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERRO ESCUTA EM TEMPO REAL] {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"[ERRO SALVAR SENSORES] {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"[ERRO STATUS SISTEMA] {ex.Message}");
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
            }
            catch (Exception ex)
            {
                _isUpdatingProgrammatically = false;
                System.Diagnostics.Debug.WriteLine($"[ERRO TOGGLE GERAL] {ex.Message}");
            }
        }

        private async void OnSensorToggled(object sender, ToggledEventArgs e)
        {
            if (_isUpdatingProgrammatically) return;

            try
            {
                if (sender is Switch switchOriginal)
                {
                    string nomeSensor = "Sensor";

                    if (switchOriginal == SwitchPresenca)
                    {
                        SistemaService.PresencaAtivo = e.Value;
                        nomeSensor = "Sensor de Presença";
                    }
                    else if (switchOriginal == SwitchCalor)
                    {
                        SistemaService.CalorAtivo = e.Value;
                        nomeSensor = "Sensor de Calor";
                    }
                    else if (switchOriginal == SwitchAlarme)
                    {
                        SistemaService.AlarmeAtivo = e.Value;
                        nomeSensor = "Alarme";
                    }

                    // 1. Salva o estado atual na coleção "Sensores/estado"
                    await SalvarEstadoSensoresNoFirebase();

                    // 2. Registra o evento de ligar/desligar no Histórico/Logs
                    SistemaService.AdicionarLog(nomeSensor, e.Value);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERRO TOGGLE SENSOR] {ex.Message}");
            }
        }

        private async void OnVerLogsClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new LogsPage());
        }

        private async void OnVerMembrosClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new MembrosFamiliaPage());
        }

        private async void OnLigarEmergenciaClicked(object sender, EventArgs e)
        {
            try
            {
                // Trava de segurança para evitar ligações acidentais
                bool confirmar = await DisplayAlert(
                    "EMERGÊNCIA",
                    "Deseja realmente discar para o 190 (Polícia Militar)?",
                    "Sim, Ligar",
                    "Cancelar");

                if (confirmar)
                {
                    if (PhoneDialer.Default.IsSupported)
                    {
                        PhoneDialer.Default.Open("190");
                    }
                    else
                    {
                        await DisplayAlert("Erro", "O recurso não é suportado neste dispositivo.", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERRO DISCAGEM 190] {ex.Message}");
                await DisplayAlert("Erro", "Não foi possível abrir o discador de telefone.", "OK");
            }
        }
    }
}
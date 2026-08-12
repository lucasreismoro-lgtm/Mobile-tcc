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
    public partial class DashboardPage : ContentPage // Gerencia o fluxo e eventos visuais do Dashboard mobile
    {
        private IDispatcherTimer timer; // Timer em segundo plano para checagem de conexões
        private bool _isUpdatingProgrammatically = false; // Trava contra disparos em loop dos eventos de switch

        public DashboardPage(string nome, string cargo) // Construtor principal carregando credenciais da sessão
        {
            InitializeComponent(); // Carrega o layout definido em XAML

            LblNomeUsuario.Text = string.IsNullOrWhiteSpace(nome) ? "Usuário" : nome; // Define nome na UI com fallback

            if (!string.IsNullOrWhiteSpace(cargo)) // Valida e exibe cargo formatado em maiúsculas
            {
                LblCargoUsuario.Text = cargo.Replace("_", " ").ToUpper(); // Substitui underlines por espaços
            }
            else
            {
                LblCargoUsuario.Text = "DONO DA CASA"; // Cargo padrão em branco
            }

            Preferences.Set("CargoUsuario", cargo ?? "dono"); // Persiste o cargo localmente para verificação de regras

            timer = Dispatcher.CreateTimer(); // Instancia o timer
            timer.Interval = TimeSpan.FromSeconds(5); // Define disparo a cada 5s
            timer.Tick += (s, e) => VerificarSistemaAutomaticamente(); // Associa função de monitoramento
            timer.Start(); // Inicia execução
        }

        public DashboardPage() : this("Usuário", "dono") // Construtor de fallback sem parâmetros
        {
        }

        protected override async void OnAppearing() // Executado automaticamente ao renderizar a tela
        {
            base.OnAppearing(); // Executa rotina base
            AplicarPermissoesPorCargo(); // Trava/Libera os switches com base no cargo
            await CarregarEstadoSensoresDoFirebase(); // Busca o estado atual salvo no Firestore
            VerificarSistemaAutomaticamente(); // Checa conectividade
        }

        private void AplicarPermissoesPorCargo() // Define interatividade dos elementos visuais
        {
            string cargoUsuario = Preferences.Get("CargoUsuario", "dono").ToLower(); // Resgata cargo em caixa baixa
            bool ePermitido = cargoUsuario.Contains("dono") || cargoUsuario.Contains("admin") || cargoUsuario != "morador"; // Avalia permissão

            MainThread.BeginInvokeOnMainThread(() => // Garante alteração na UI Thread
            {
                if (SwitchGeral != null) SwitchGeral.IsEnabled = ePermitido; // Bloqueia/Libera Geral
                if (SwitchPresenca != null) SwitchPresenca.IsEnabled = ePermitido; // Bloqueia/Libera Presença
                if (SwitchCalor != null) SwitchCalor.IsEnabled = ePermitido; // Bloqueia/Libera Calor
                if (SwitchAlarme != null) SwitchAlarme.IsEnabled = ePermitido; // Bloqueia/Libera Alarme
            });
        }

        private async Task CarregarEstadoSensoresDoFirebase() // Lê a subcoleção de sensores do Firestore
        {
            try
            {
                string cpfDono = Preferences.Get("CpfDono", string.Empty); // Resgata o CPF do Dono salvo no Login
                if (string.IsNullOrEmpty(cpfDono)) return; // Aborta se CPF não estiver disponível

                // Garante que o Firebase esteja inicializado antes de consultar
                if (SistemaService.FirestoreDb == null)
                {
                    await SistemaService.InicializarFirebase();
                    if (SistemaService.FirestoreDb == null) return;
                }

                _isUpdatingProgrammatically = true; // Ativa a trava para evitar disparar Toggled no carregamento

                // Busca o documento dentro do caminho: Usuarios/{cpfDono}/Sensores/estado
                DocumentReference docRef = SistemaService.FirestoreDb.Collection("Usuarios")
                                                                     .Document(cpfDono)
                                                                     .Collection("Sensores")
                                                                     .Document("estado");

                DocumentSnapshot snapshot = await docRef.GetSnapshotAsync(); // Puxa dados da nuvem

                if (snapshot.Exists) // Se a subcoleção já existir
                {
                    snapshot.TryGetValue("presencaAtivo", out bool presenca); // Extrai o valor de presença
                    snapshot.TryGetValue("calorAtivo", out bool calor); // Extrai o valor de calor
                    snapshot.TryGetValue("alarmeAtivo", out bool alarme); // Extrai o valor de alarme

                    SistemaService.PresencaAtivo = presenca; // Atualiza a memória local no service
                    SistemaService.CalorAtivo = calor; // Atualiza a memória local no service
                    SistemaService.AlarmeAtivo = alarme; // Atualiza a memória local no service

                    MainThread.BeginInvokeOnMainThread(() => // Atualiza chaves na interface
                    {
                        if (SwitchPresenca != null) SwitchPresenca.IsToggled = presenca; // Seta botão presença
                        if (SwitchCalor != null) SwitchCalor.IsToggled = calor; // Seta botão calor
                        if (SwitchAlarme != null) SwitchAlarme.IsToggled = alarme; // Seta botão alarme
                        if (SwitchGeral != null) SwitchGeral.IsToggled = presenca && calor && alarme; // Seta botão geral se todos ativos
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao carregar sensores: {ex.Message}"); // Log de exceção
            }
            finally
            {
                _isUpdatingProgrammatically = false; // Desativa a trava com segurança
            }
        }

        private async Task SalvarEstadoSensoresNoFirebase() // Persiste qualquer alteração de switch no banco
        {
            try
            {
                string cpfDono = Preferences.Get("CpfDono", string.Empty); // Obtém CPF do Dono
                if (string.IsNullOrEmpty(cpfDono)) return;

                if (SistemaService.FirestoreDb == null)
                {
                    await SistemaService.InicializarFirebase();
                    if (SistemaService.FirestoreDb == null) return;
                }

                DocumentReference docRef = SistemaService.FirestoreDb.Collection("Usuarios") // Referência da subcoleção
                                                                     .Document(cpfDono)
                                                                     .Collection("Sensores")
                                                                     .Document("estado");

                Dictionary<string, object> dados = new Dictionary<string, object> // Dicionário atualizado
                {
                    { "presencaAtivo", SistemaService.PresencaAtivo }, // Estado do sensor de presença
                    { "calorAtivo", SistemaService.CalorAtivo }, // Estado do sensor de calor
                    { "alarmeAtivo", SistemaService.AlarmeAtivo } // Estado do alarme sonoro
                };

                await docRef.SetAsync(dados, SetOptions.MergeAll); // Salva ou mescla os dados na nuvem
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao salvar sensores: {ex.Message}"); // Imprime log
            }
        }

        private void VerificarSistemaAutomaticamente() // Checa conectividade de rede e Firebase
        {
            try
            {
                bool temInternet = Connectivity.Current.NetworkAccess == NetworkAccess.Internet; // Confirma se há acesso à internet
                bool firebaseOnline = SistemaService.IsFirebaseConectado; // Confirma conexão com Firebase

                MainThread.BeginInvokeOnMainThread(() => // Aplica alterações na UI
                {
                    if (temInternet && firebaseOnline)
                    {
                        StatusIndicator.Fill = Colors.Green; // Cor verde para status online
                        StatusLabel.Text = "Sistema Online"; // Texto status ok
                    }
                    else if (temInternet && !firebaseOnline)
                    {
                        StatusIndicator.Fill = Colors.Yellow; // Cor amarela para alerta
                        StatusLabel.Text = "Atenção: Firebase Offline"; // Sinaliza erro do banco
                    }
                    else
                    {
                        StatusIndicator.Fill = Colors.Red; // Cor vermelha sem internet
                        StatusLabel.Text = "Sistema Offline (Sem Rede)"; // Sinaliza falta de conexão
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro status: {ex.Message}"); // Trata exceção de checagem
            }
        }

        private async void OnGeralToggled(object sender, ToggledEventArgs e) // Evento disparado no Switch Geral
        {
            if (_isUpdatingProgrammatically) return; // Ignores disparos efetuados via código C#

            try
            {
                bool estado = e.Value; // Captura novo valor (true/false)

                _isUpdatingProgrammatically = true; // Ativa trava
                if (SwitchPresenca != null) SwitchPresenca.IsToggled = estado; // Ajusta presença
                if (SwitchCalor != null) SwitchCalor.IsToggled = estado; // Ajusta calor
                if (SwitchAlarme != null) SwitchAlarme.IsToggled = estado; // Ajusta alarme
                _isUpdatingProgrammatically = false; // Desativa trava

                SistemaService.PresencaAtivo = estado; // Seta presença na memória
                SistemaService.CalorAtivo = estado; // Seta calor na memória
                SistemaService.AlarmeAtivo = estado; // Seta alarme na memória

                await SalvarEstadoSensoresNoFirebase(); // Grava a atualização em lote no Firestore
                SistemaService.AdicionarLog("Sistema Geral", estado); // Gera registro de log
            }
            catch (Exception ex)
            {
                _isUpdatingProgrammatically = false; // Libera trava em caso de falha
                System.Diagnostics.Debug.WriteLine($"Erro Geral: {ex.Message}"); // Log de erro
            }
        }

        private async void OnSensorToggled(object sender, ToggledEventArgs e) // Evento disparado em switches individuais
        {
            if (_isUpdatingProgrammatically) return; // Aborta se veio de alteração pelo Controle Geral

            try
            {
                if (sender is Switch switchOriginal) // Mapeia a origem do clique
                {
                    if (switchOriginal == SwitchPresenca) // Caso altere o sensor de presença
                    {
                        SistemaService.PresencaAtivo = e.Value; // Atualiza variável
                        SistemaService.AdicionarLog("Sensor de Presença", e.Value); // Registra no histórico
                    }
                    else if (switchOriginal == SwitchCalor) // Caso altere o sensor de calor
                    {
                        SistemaService.CalorAtivo = e.Value; // Atualiza variável
                        SistemaService.AdicionarLog("Sensor de Calor", e.Value); // Registra no histórico
                    }
                    else if (switchOriginal == SwitchAlarme) // Caso altere o alarme
                    {
                        SistemaService.AlarmeAtivo = e.Value; // Atualiza variável
                        SistemaService.AdicionarLog("Alarme Sonoro", e.Value); // Registra no histórico
                    }

                    await SalvarEstadoSensoresNoFirebase(); // Grava a mudança individual no Firestore
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro Sensor: {ex.Message}"); // Log de exceção
            }
        }

        private async void OnVerLogsClicked(object sender, EventArgs e) // Abre histórico
        {
            await Navigation.PushAsync(new LogsPage()); // Navega para tela LogsPage
        }

        private void OnSimularSensorClicked(object sender, EventArgs e) // Simulação manual do sensor de movimento
        {
            SistemaService.RegistrarEventoExterno("Sensor de Presença", "MOVIMENTO DETECTADO!"); // Gera entrada de teste
            DisplayAlert("Alerta!", "Movimento detectado pelo sensor", "OK"); // Exibe alerta pop-up
        }

        private async void OnVerMembrosClicked(object sender, EventArgs e) // Abre membros da família
        {
            await Navigation.PushAsync(new MembrosFamiliaPage()); // Navega para MembrosFamiliaPage
        }
    }
}
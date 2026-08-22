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
        private IDispatcherTimer? _timer; // Timer para execução de tarefas periódicas de checagem
        private bool _isUpdatingProgrammatically = false; // Flag de controle para evitar loops ao alterar switches via código

        // Instância do serviço de escuta da coleção de Eventos
        private readonly SensorAlertService _alertService = new SensorAlertService(); // Instancia o serviço de escuta de alertas

        // Variável de controle para ignorar o snapshot inicial da Dashboard
        private bool _primeiraCargaSensores = true; // Flag que evita re-disparar pop-ups no carregamento inicial da página

        // Ouvinte em tempo real para mudanças de estado dos switches no Firestore
        private FirestoreChangeListener? _listenerSensores; // Armazena o ouvinte em tempo real do estado dos sensores

        public DashboardPage(string nome, string cargo) // Construtor principal que recebe nome e cargo do usuário logado
        {
            InitializeComponent(); // Inicializa e carrega a interface gráfica definida no XAML

            if (LblNomeUsuario != null) // Valida se o Label do nome existe no Layout XAML
                LblNomeUsuario.Text = string.IsNullOrWhiteSpace(nome) ? "Usuário" : nome; // Define o nome do usuário na tela ou "Usuário" como padrão

            if (LblCargoUsuario != null) // Valida se o Label do cargo existe no Layout XAML
            {
                LblCargoUsuario.Text = string.IsNullOrWhiteSpace(cargo) // Define o cargo formatado no Label
                    ? "DONO DA CASA" // Fallback padrão caso o cargo esteja em branco
                    : cargo.Replace("_", " ").ToUpper(); // Substitui underlines por espaço e converte texto para maiúsculo
            }

            Preferences.Set("CargoUsuario", cargo ?? "dono"); // Armazena o cargo do usuário na memória local do dispositivo

            ConfigurarTimer(); // Chama o método de configuração do timer de verificação
        }

        public DashboardPage() : this("Usuário", "dono") // Construtor secundário padrão com parâmetros default
        {
        }

        private void ConfigurarTimer() // Cria e agenda o timer de monitoramento automático
        {
            _timer = Dispatcher.CreateTimer(); // Cria uma nova instância de timer do MAUI
            _timer.Interval = TimeSpan.FromSeconds(5); // Define o intervalo de disparo do timer para 5 segundos
            _timer.Tick += (s, e) => VerificarSistemaAutomaticamente(); // Associa o evento de disparo do timer ao método de verificação
        }

        protected override async void OnAppearing() // Método acionado automaticamente ao exibir a página na tela
        {
            base.OnAppearing(); // Chama a implementação original da classe base

            _timer?.Start(); // Inicia o timer periódico de verificação de status
            AplicarPermissoesPorCargo(); // Ajusta os controles ativando ou desativando switches conforme o cargo
            await CarregarEstadoSensoresDoFirebase(); // Busca o estado atualizado dos sensores no banco de dados

            // Reseta a trava ao entrar na página
            _primeiraCargaSensores = true; // Reinicia a flag de controle da primeira carga
            IniciarEscutaSensoresEmTempoReal(); // Conecta a escuta em tempo real no Firestore para sincronização dos switches

            VerificarSistemaAutomaticamente(); // Executa uma verificação imediata da conexão de rede e do banco

            // Inicia a escuta em tempo real filtrada do SensorAlertService
            string cpfDono = Preferences.Get("CpfDonoCasa", string.Empty); // Recupera o CPF do proprietário armazenado localmente
            if (!string.IsNullOrEmpty(cpfDono) && SistemaService.FirestoreDb != null) // Verifica se o CPF e a instância do banco são válidos
            {
                _alertService.IniciarEscutaEventos(cpfDono, SistemaService.FirestoreDb); // Inicia a escuta filtrada dos alertas para a residência
            }
        }

        protected override async void OnDisappearing() // Método acionado automaticamente quando a página deixa de ser exibida
        {
            base.OnDisappearing(); // Executa o comportamento base da página
            _timer?.Stop(); // Pausa a execução do timer de status para economizar recursos

            // Interrompe as escutas ao sair da página para evitar vazamentos e alertas em lote
            _alertService.PararEscuta(); // Cancela o serviço de escuta de eventos

            if (_listenerSensores != null) // Checa se a escuta de estado dos sensores está ativa
            {
                await _listenerSensores.StopAsync(); // Encerra a escuta em tempo real dos sensores assincronamente
                _listenerSensores = null; // Limpa a referência do ouvinte
            }
        }

        private void AplicarPermissoesPorCargo() // Aplica controle de acesso na interface com base no cargo
        {
            string cargoUsuario = Preferences.Get("CargoUsuario", "dono").ToLower(); // Obtém o cargo gravado e converte para minúsculas
            bool ePermitido = cargoUsuario.Contains("dono") || cargoUsuario.Contains("admin") || cargoUsuario != "morador"; // Avalia se o usuário tem permissão para alterar estados

            MainThread.BeginInvokeOnMainThread(() => // Direciona a atualização visual obrigatoriamente para a Thread principal (UI)
            {
                if (SwitchGeral != null) SwitchGeral.IsEnabled = ePermitido; // Ativa ou desativa o Switch Geral conforme a permissão
                if (SwitchPresenca != null) SwitchPresenca.IsEnabled = ePermitido; // Ativa ou desativa o Switch de Presença conforme a permissão
                if (SwitchCalor != null) SwitchCalor.IsEnabled = ePermitido; // Ativa ou desativa o Switch de Calor conforme a permissão
                if (SwitchAlarme != null) SwitchAlarme.IsEnabled = ePermitido; // Ativa ou desativa o Switch do Alarme conforme a permissão
            });
        }

        private async Task CarregarEstadoSensoresDoFirebase() // Lê as configurações vigentes dos sensores direto no banco
        {
            try
            {
                string cpfDono = Preferences.Get("CpfDonoCasa", string.Empty); // Obtém o CPF do Dono gravado no dispositivo
                if (string.IsNullOrEmpty(cpfDono)) return; // Cancela a busca se não houver CPF registrado

                if (SistemaService.FirestoreDb == null) // Checa se o serviço do Firestore está desconectado
                {
                    await SistemaService.InicializarFirebase(); // Tenta reconectar e inicializar o Firebase
                    if (SistemaService.FirestoreDb == null) return; // Se continuar nulo, encerra a operação
                }

                _isUpdatingProgrammatically = true; // Seta a trava programática para impedir disparos acidentais dos eventos dos switches

                DocumentReference docRef = SistemaService.FirestoreDb // Monta o caminho do documento de estado dos sensores no Firestore
                    .Collection("Usuarios") // Coleção de usuários
                    .Document(cpfDono) // Documento específico do dono
                    .Collection("Sensores") // Subcoleção de sensores
                    .Document("estado"); // Documento com os estados atuais

                DocumentSnapshot snapshot = await docRef.GetSnapshotAsync(); // Busca os dados do documento assincronamente

                if (snapshot.Exists) // Verifica se o documento de estado foi retornado
                {
                    snapshot.TryGetValue("presencaAtivo", out bool presenca); // Extrai o valor do sensor de presença
                    snapshot.TryGetValue("calorAtivo", out bool calor); // Extrai o valor do sensor de calor
                    snapshot.TryGetValue("alarmeAtivo", out bool alarme); // Extrai o valor do alarme

                    SistemaService.PresencaAtivo = presenca; // Atualiza a variável global do sensor de presença
                    SistemaService.CalorAtivo = calor; // Atualiza a variável global do sensor de calor
                    SistemaService.AlarmeAtivo = alarme; // Atualiza a variável global do alarme

                    MainThread.BeginInvokeOnMainThread(() => // Garante a atualização dos elementos visuais na UI Thread
                    {
                        if (SwitchPresenca != null) SwitchPresenca.IsToggled = presenca; // Atualiza a posição visual do Switch Presença
                        if (SwitchCalor != null) SwitchCalor.IsToggled = calor; // Atualiza a posição visual do Switch Calor
                        if (SwitchAlarme != null) SwitchAlarme.IsToggled = alarme; // Atualiza a posição visual do Switch Alarme
                        if (SwitchGeral != null) SwitchGeral.IsToggled = presenca && calor && alarme; // Liga o Geral somente se todos estiverem ativos
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERRO CARREGAR SENSORES] {ex.Message}"); // Registra a exceção no terminal de depuração
            }
            finally
            {
                _isUpdatingProgrammatically = false; // Libera a trava programática após finalizar o carregamento
            }
        }

        private void IniciarEscutaSensoresEmTempoReal() // Abre escuta contínua de alterações nos sensores
        {
            try
            {
                string cpfDono = Preferences.Get("CpfDonoCasa", string.Empty); // Recupera o CPF do Dono gravado
                if (string.IsNullOrEmpty(cpfDono) || SistemaService.FirestoreDb == null) return; // Valida dados mínimos de inicialização

                DocumentReference docRef = SistemaService.FirestoreDb // Aponta para o documento de estado dos sensores
                    .Collection("Usuarios") // Coleção principal
                    .Document(cpfDono) // Identificador do proprietário
                    .Collection("Sensores") // Subcoleção
                    .Document("estado"); // Documento monitorado

                _listenerSensores = docRef.Listen(snapshot => // Cria o ouvinte reativo em tempo real para o documento
                {
                    if (snapshot.Exists) // Checa se os dados do snapshot são válidos
                    {
                        snapshot.TryGetValue("presencaAtivo", out bool presenca); // Obtém o estado atualizado da presença
                        snapshot.TryGetValue("calorAtivo", out bool calor); // Obtém o estado atualizado do calor
                        snapshot.TryGetValue("alarmeAtivo", out bool alarme); // Obtém o estado atualizado do alarme

                        SistemaService.PresencaAtivo = presenca; // Sincroniza o estado de presença na memória
                        SistemaService.CalorAtivo = calor; // Sincroniza o estado de calor na memória
                        SistemaService.AlarmeAtivo = alarme; // Sincroniza o estado do alarme na memória

                        MainThread.BeginInvokeOnMainThread(() => // Executa a alteração dos componentes na UI Thread
                        {
                            _isUpdatingProgrammatically = true; // Seta trava para evitar o disparo dos eventos manuais do usuário
                            if (SwitchPresenca != null) SwitchPresenca.IsToggled = presenca; // Ajusta o interruptor de Presença
                            if (SwitchCalor != null) SwitchCalor.IsToggled = calor; // Ajusta o interruptor de Calor
                            if (SwitchAlarme != null) SwitchAlarme.IsToggled = alarme; // Ajusta o interruptor do Alarme
                            if (SwitchGeral != null) SwitchGeral.IsToggled = presenca && calor && alarme; // Sincroniza o interruptor Geral
                            _isUpdatingProgrammatically = false; // Desativa a trava de atualização programática
                        });

                        // Trava a primeira execução do listener para não re-disparar pop-ups ao carregar a página
                        if (_primeiraCargaSensores) // Checa se é a primeira leitura do ouvinte
                        {
                            _primeiraCargaSensores = false; // Desmarca a flag indicando que a carga inicial já foi tratada
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERRO ESCUTA EM TEMPO REAL] {ex.Message}"); // Grava mensagens de erro no console do depurador
            }
        }

        private async Task SalvarEstadoSensoresNoFirebase() // Escreve o estado dos switches no Firestore
        {
            try
            {
                string cpfDono = Preferences.Get("CpfDonoCasa", string.Empty); // Recupera o CPF da residência localmente
                if (string.IsNullOrEmpty(cpfDono)) return; // Cancela se o CPF não existir

                if (SistemaService.FirestoreDb == null) // Checa status da conexão com o banco
                {
                    await SistemaService.InicializarFirebase(); // Tenta reestabelecer conexão
                    if (SistemaService.FirestoreDb == null) return; // Se falhar, abandona o salvamento
                }

                DocumentReference docRef = SistemaService.FirestoreDb // Instancia a referência do documento de estado dos sensores
                    .Collection("Usuarios") // Coleção
                    .Document(cpfDono) // Documento do dono
                    .Collection("Sensores") // Subcoleção
                    .Document("estado"); // Documento final

                Dictionary<string, object> dados = new Dictionary<string, object> // Monta o dicionário de pares chave/valor a gravar
                {
                    { "presencaAtivo", SistemaService.PresencaAtivo }, // Armazena o estado do sensor de presença
                    { "calorAtivo", SistemaService.CalorAtivo }, // Armazena o estado do sensor de calor
                    { "alarmeAtivo", SistemaService.AlarmeAtivo } // Armazena o estado do alarme
                };

                await docRef.SetAsync(dados, SetOptions.MergeAll); // Grava dados no banco mesclando com campos já existentes
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERRO SALVAR SENSORES] {ex.Message}"); // Grava o erro de salvamento na depuração
            }
        }

        private void VerificarSistemaAutomaticamente() // Atualiza os indicadores visuais da conectividade geral do sistema
        {
            try
            {
                bool temInternet = Connectivity.Current.NetworkAccess == NetworkAccess.Internet; // Checa se há acesso à internet no celular
                bool firebaseOnline = SistemaService.IsFirebaseConectado; // Verifica a flag de status da conexão do Firebase

                MainThread.BeginInvokeOnMainThread(() => // Direciona para a UI Thread para alterar cores e textos da tela
                {
                    if (StatusIndicator == null || StatusLabel == null) return; // Evita exceções caso a UI não esteja pronta

                    if (temInternet && firebaseOnline) // Caso haja internet e o banco esteja operando normalmente
                    {
                        StatusIndicator.Fill = Colors.Green; // Seta indicador gráfico para a cor verde
                        StatusLabel.Text = "Sistema Online"; // Seta a legenda para "Sistema Online"
                    }
                    else if (temInternet && !firebaseOnline) // Caso tenha internet porém o banco esteja offline
                    {
                        StatusIndicator.Fill = Colors.Yellow; // Seta indicador gráfico para a cor amarela
                        StatusLabel.Text = "Atenção: Firebase Offline"; // Seta aviso no texto do sistema
                    }
                    else // Caso esteja completamente sem conexão com a internet
                    {
                        StatusIndicator.Fill = Colors.Red; // Seta indicador gráfico para a cor vermelha
                        StatusLabel.Text = "Sistema Offline (Sem Rede)"; // Seta legenda de falha total de rede
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERRO STATUS SISTEMA] {ex.Message}"); // Escreve no log de depuração em caso de falhas
            }
        }

        private async void OnGeralToggled(object sender, ToggledEventArgs e) // Evento acionado ao alternar o Switch Geral
        {
            if (_isUpdatingProgrammatically) return; // Cancela a execução se o disparo foi feito via código

            try
            {
                bool estado = e.Value; // Captura o novo estado (true/false) do switch Geral

                _isUpdatingProgrammatically = true; // Ativa a trava para alterar os outros switches sem disparar seus eventos
                if (SwitchPresenca != null) SwitchPresenca.IsToggled = estado; // Sincroniza o switch de Presença com o valor Geral
                if (SwitchCalor != null) SwitchCalor.IsToggled = estado; // Sincroniza o switch de Calor com o valor Geral
                if (SwitchAlarme != null) SwitchAlarme.IsToggled = estado; // Sincroniza o switch de Alarme com o valor Geral
                _isUpdatingProgrammatically = false; // Desativa a trava programática

                SistemaService.PresencaAtivo = estado; // Atualiza o estado global de presença
                SistemaService.CalorAtivo = estado; // Atualiza o estado global de calor
                SistemaService.AlarmeAtivo = estado; // Atualiza o estado global do alarme

                await SalvarEstadoSensoresNoFirebase(); // Salva todos os estados unificados no Firestore
            }
            catch (Exception ex)
            {
                _isUpdatingProgrammatically = false; // Restaura a trava de segurança em caso de erro
                System.Diagnostics.Debug.WriteLine($"[ERRO TOGGLE GERAL] {ex.Message}"); // Grava exceção na janela de saída
            }
        }

        private async void OnSensorToggled(object sender, ToggledEventArgs e) // Evento acionado ao alternar um switch individual de sensor
        {
            if (_isUpdatingProgrammatically) return; // Cancela se a alteração foi gerada pelo sistema

            try
            {
                if (sender is Switch switchOriginal) // Converte e valida a origem do evento como um Switch do MAUI
                {
                    string nomeSensor = "Sensor"; // Valor padrão para o nome do sensor

                    if (switchOriginal == SwitchPresenca) // Verifica se o disparador foi o switch de Presença
                    {
                        SistemaService.PresencaAtivo = e.Value; // Atualiza o estado na memória global
                        nomeSensor = "Sensor de Presença"; // Define a legenda para histórico
                    }
                    else if (switchOriginal == SwitchCalor) // Verifica se o disparador foi o switch de Calor
                    {
                        SistemaService.CalorAtivo = e.Value; // Atualiza o estado na memória global
                        nomeSensor = "Sensor de Calor"; // Define a legenda para histórico
                    }
                    else if (switchOriginal == SwitchAlarme) // Verifica se o disparador foi o switch de Alarme
                    {
                        SistemaService.AlarmeAtivo = e.Value; // Atualiza o estado na memória global
                        nomeSensor = "Alarme"; // Define a legenda para histórico
                    }

                    // 1. Salva o estado atual na coleção "Sensores/estado"
                    await SalvarEstadoSensoresNoFirebase(); // Escreve o estado alterado no Firestore

                    // 2. Registra o evento de ligar/desligar no Histórico/Logs
                    SistemaService.AdicionarLog(nomeSensor, e.Value); // Registra a ação no log local e remoto
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERRO TOGGLE SENSOR] {ex.Message}"); // Grava falhas no log do depurador
            }
        }

        private async void OnVerLogsClicked(object sender, EventArgs e) // Disparado pelo clique no botão de Logs
        {
            await Navigation.PushAsync(new LogsPage()); // Navega o usuário para a tela de visualização de histórico de Logs
        }

        private async void OnVerMembrosClicked(object sender, EventArgs e) // Disparado pelo clique no botão de Membros
        {
            await Navigation.PushAsync(new MembrosFamiliaPage()); // Navega o usuário para a tela de gerenciamento de membros
        }

        private async void OnLigarEmergenciaClicked(object sender, EventArgs e) // Disparado pelo clique no botão de Emergência (190)
        {
            try
            {
                // Trava de segurança para evitar ligações acidentais
                bool confirmar = await DisplayAlert( // Solicita confirmação explícita ao usuário por caixa de diálogo
                    "EMERGÊNCIA", // Título do alerta
                    "Deseja realmente discar para o 190 (Polícia Militar)?", // Mensagem
                    "Sim, Ligar", // Texto de confirmação
                    "Cancelar"); // Texto de cancelamento

                if (confirmar) // Caso o usuário clique em "Sim, Ligar"
                {
                    if (PhoneDialer.Default.IsSupported) // Verifica se o dispositivo atual possui suporte a discagem telefônica
                    {
                        PhoneDialer.Default.Open("190"); // Abre o discador nativo com o número 190 preenchido
                    }
                    else
                    {
                        await DisplayAlert("Erro", "O recurso não é suportado neste dispositivo.", "OK"); // Alerta de incompatibilidade do aparelho
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERRO DISCAGEM 190] {ex.Message}"); // Log de erro no ambiente de teste
                await DisplayAlert("Erro", "Não foi possível abrir o discador de telefone.", "OK"); // Alerta de erro na abertura da discagem
            }
        }
    }
}
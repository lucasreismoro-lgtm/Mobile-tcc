using System; 
using System.Collections.Generic; 
using System.Collections.ObjectModel; 
using System.Diagnostics; 
using System.IO;
using System.Threading.Tasks; 
using Google.Cloud.Firestore; 
using Microsoft.Maui.ApplicationModel; 
using Microsoft.Maui.Graphics; 
using Microsoft.Maui.Storage;

namespace dfgger // Declaração do namespace do projeto
{
    public static class SistemaService // Classe estática global para gerenciar dados e conexão Firebase
    {
        public static FirestoreDb? FirestoreDb { get; set; } // Guarda a instância ativa da conexão com o banco
        public static bool IsFirebaseConectado => FirestoreDb != null; // Propriedade que indica se a conexão foi estabelecida
        public static string CpfUsuarioAtual { get; set; } = string.Empty; // Armazena o CPF do usuário logado na sessão

        public static bool PresencaAtivo { get; set; } = false; // Estado global do sensor de presença
        public static bool CalorAtivo { get; set; } = false; // Estado global do sensor de calor
        public static bool AlarmeAtivo { get; set; } = false; // Estado global do sistema de alarme

        // Lista observável conectada à tela de histórico (atualiza a UI automaticamente ao ser modificada)
        public static ObservableCollection<EventoLog> ListaDeLogs { get; set; } = new ObservableCollection<EventoLog>();

        // Método assíncrono para autenticar e conectar ao Firebase Firestore
        public static async Task InicializarFirebase()
        {
            try // Inicia bloco de tratamento de erros
            {
                if (FirestoreDb != null) return; // Se já estiver conectado, encerra a execução do método

                // Abre o arquivo de credenciais JSON armazenado nos pacotes do aplicativo
                using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json");
                // Cria as credenciais de acesso OAuth2 usando a chave privada da conta de serviço
                var credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromStream(stream)
                    .CreateScoped("https://www.googleapis.com/auth/datastore");

                // Configura o construtor do banco definindo o ID do projeto e as credenciais
                FirestoreDbBuilder builder = new FirestoreDbBuilder
                {
                    ProjectId = "banco-tcc-dc633", // ID do projeto registrado no console do Firebase
                    Credential = credential // Credencial de autenticação gerada
                };

                FirestoreDb = await builder.BuildAsync(); // Instancia o objeto de banco de dados assincronamente
                Debug.WriteLine("[FIREBASE] Conectado com sucesso!"); // Exibe mensagem de sucesso no Output/Console
            }
            catch (Exception ex) // Captura falhas de conexão ou leitura do arquivo de chave
            {
                Debug.WriteLine($"[ERRO FIREBASE INICIALIZACAO] {ex.Message}"); // Exibe a mensagem de erro no Output
                FirestoreDb = null; // Define a instância como nula garantindo o estado desconectado
            }
        }

        // Retorna a instância atual do banco Firestore
        public static FirestoreDb? ObterInstanciaFirestore()
        {
            return FirestoreDb; // Retorna o objeto de conexão
        }

        // Método assíncrono para buscar o histórico de eventos armazenado na nuvem
        public static async Task CarregarLogsDoFirebaseAsync()
        {
            try // Inicia bloco de tratamento de erros
            {
                if (FirestoreDb == null) // Se a conexão não existir...
                    await InicializarFirebase(); // Tenta conectar primeiro

                if (FirestoreDb == null) return; // Cancela se a tentativa de reconexão falhar

                // Obtém o CPF da propriedade global ou resgata do armazenamento local do aparelho
                string cpfDono = string.IsNullOrEmpty(CpfUsuarioAtual)
                    ? Preferences.Get("CpfDonoCasa", string.Empty)
                    : CpfUsuarioAtual;

                if (string.IsNullOrEmpty(cpfDono)) return; // Cancela se nenhum CPF for identificado

                // Cria uma referência apontando para a subcoleção "Eventos" do usuário específico no Firestore
                CollectionReference eventosRef = FirestoreDb.Collection("Usuarios")
                                                            .Document(cpfDono)
                                                            .Collection("Eventos");

                QuerySnapshot snapshot = await eventosRef.GetSnapshotAsync(); // Busca todos os documentos da coleção

                // Executa a atualização da interface do usuário obrigatoriamente na Thread Principal
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ListaDeLogs.Clear(); // Limpa a lista local de logs para evitar itens duplicados

                    foreach (DocumentSnapshot doc in snapshot.Documents) // Percorre todos os documentos retornados
                    {
                        if (doc.Exists) // Confirma se o documento é válido e possui dados
                        {
                            // Extrai o nome do sensor (usa "Sistema" se o campo não existir)
                            string sensor = doc.ContainsField("sensor") ? doc.GetValue<string>("sensor") : "Sistema";
                            // Extrai a mensagem cadastrada (usa o texto padrão se ausente)
                            string mensagem = doc.ContainsField("mensagem") ? doc.GetValue<string>("mensagem") : "Evento registrado";

                            DateTime dataHora = DateTime.Now; // Define a data padrão como a atual do aparelho
                            // Verifica e converte o carimbo de data/hora (Timestamp) do Firebase para o horário local
                            if (doc.ContainsField("dataHora") && doc.GetValue<Timestamp?>("dataHora").HasValue)
                            {
                                dataHora = doc.GetValue<Timestamp>("dataHora").ToDateTime().ToLocalTime();
                            }

                            Color corStatus = Colors.Gray; // Define a cor cinza como padrão inicial da tag do evento
                            // Define a cor visual conforme a origem do sensor ou tipo de mensagem
                            if (sensor.Contains("Presença", StringComparison.OrdinalIgnoreCase))
                                corStatus = Colors.Orange; // Laranja para registros do sensor de presença
                            else if (sensor.Contains("Calor", StringComparison.OrdinalIgnoreCase))
                                corStatus = Colors.Red; // Vermelho para alertas de calor/temperatura
                            else if (sensor.Contains("Sistema", StringComparison.OrdinalIgnoreCase) || mensagem.Contains("Ativado", StringComparison.OrdinalIgnoreCase))
                                corStatus = Colors.Green; // Verde para ativações ou ações do sistema

                            // Insere o evento no topo da lista exibida no aplicativo
                            ListaDeLogs.Insert(0, new EventoLog
                            {
                                Titulo = $"[{sensor}] {mensagem}", // Formata a string de título do evento
                                Horario = dataHora.ToString("dd/MM/yyyy HH:mm:ss"), // Formata a data para exibição
                                StatusColor = corStatus // Define a cor calculada para o indicador
                            });
                        }
                    }
                });
            }
            catch (Exception ex) // Captura erros no carregamento dos dados
            {
                Debug.WriteLine($"[ERRO CARREGAR LOGS] {ex.Message}"); // Exibe a exceção ocorrida no console
            }
        }

        // Método assíncrono que adiciona o log na interface e salva no Firebase
        public static async Task RegistrarEventoAsync(string sensor, string mensagem)
        {
            // Adiciona o novo evento na UI imediatamente para garantir uma resposta rápida ao usuário
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ListaDeLogs.Insert(0, new EventoLog // Adiciona no início da lista local
                {
                    Titulo = $"[{sensor}] {mensagem}", // Define o título formatado
                    Horario = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), // Registra a hora atual
                    StatusColor = mensagem.Contains("Ativado", StringComparison.OrdinalIgnoreCase) ? Colors.Green : Colors.Gray // Define a cor conforme o estado
                });
            });

            try // Inicia bloco de gravação no banco
            {
                if (FirestoreDb == null) await InicializarFirebase(); // Garante a conexão ativa
                if (FirestoreDb == null) return; // Aborta caso não consiga conectar

                // Identifica qual o usuário proprietário do registro
                string cpfDono = string.IsNullOrEmpty(CpfUsuarioAtual)
                    ? Preferences.Get("CpfDonoCasa", string.Empty)
                    : CpfUsuarioAtual;

                if (string.IsNullOrEmpty(cpfDono)) return; // Aborta se não houver usuário identificado

                // Monta a estrutura de chave/valor com as informações que serão gravadas
                var novoLog = new Dictionary<string, object>
                {
                    { "sensor", sensor }, // Nome/tipo do dispositivo gerador
                    { "mensagem", mensagem }, // Detalhes da ocorrência
                    { "dataHora", Timestamp.FromDateTime(DateTime.UtcNow) } // Data/hora universal da gravação
                };

                // Envia e salva o novo documento na subcoleção "Eventos" dentro do nó do usuário no Firestore
                await FirestoreDb.Collection("Usuarios")
                                .Document(cpfDono)
                                .Collection("Eventos")
                                .AddAsync(novoLog);
            }
            catch (Exception ex) // Captura falhas de envio para a nuvem
            {
                Debug.WriteLine($"[ERRO REGISTRAR EVENTO NO FIREBASE] {ex.Message}"); // Exibe o erro no console
            }
        }

        // Método auxiliar simplificado para registrar a alteração de estado (ativado/desativado) de um componente
        public static void AdicionarLog(string componente, bool estado)
        {
            string acao = estado ? "Ativado" : "Desativado"; // Converte o booleano em texto descritivo
            string mensagem = $"{componente} foi {acao}."; // Cria a frase explicativa
            _ = RegistrarEventoAsync(componente, mensagem); // Executa a gravação assíncrona ignorando o retorno (Fire-and-forget)
        }

        // Método de atalho para receber e registrar chamadas vindas de outros módulos
        public static void RegistrarEventoExterno(string sensor, string mensagem)
        {
            _ = RegistrarEventoAsync(sensor, mensagem); // Executa a gravação assíncrona em segundo plano
        }
    }
}
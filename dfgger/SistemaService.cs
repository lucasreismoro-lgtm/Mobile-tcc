using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using Google.Cloud.Firestore;
using Google.Apis.Auth.OAuth2;

namespace dfgger
{
    public static class SistemaService
    {
        // Instância global do Firestore para a DashboardPage e outras páginas
        public static FirestoreDb? FirestoreDb { get; set; }

        // Estados dos sensores em memória
        public static bool PresencaAtivo { get; set; } = true;
        public static bool CalorAtivo { get; set; } = true;
        public static bool AlarmeAtivo { get; set; } = true;
        public static bool IsFirebaseConectado { get; set; } = false;

        // Lista de logs exibida na interface
        public static ObservableCollection<EventoLog> ListaDeLogs { get; set; } = new ObservableCollection<EventoLog>();

        // ================= INICIALIZAÇÃO DO FIREBASE =================
        public static async Task InicializarFirebase()
        {
            if (FirestoreDb != null) return;

            try
            {
                // 1. Abre o arquivo conexao.json da pasta Raw como Stream binário
                using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json");

                // 2. Lê a credencial injetando o escopo obrigatório do Firestore
                var credential = GoogleCredential.FromStream(stream)
                    .CreateScoped("https://www.googleapis.com/auth/datastore");

                // 3. Constrói o cliente de banco de dados
                var builder = new FirestoreDbBuilder
                {
                    ProjectId = "banco-tcc-dc633",
                    Credential = credential
                };

                FirestoreDb = await builder.BuildAsync();
                IsFirebaseConectado = true;
                System.Diagnostics.Debug.WriteLine("Firebase (SistemaService) inicializado com sucesso!");
            }
            catch (Exception ex)
            {
                IsFirebaseConectado = false;
                System.Diagnostics.Debug.WriteLine($"Erro ao inicializar Firebase no SistemaService: {ex.Message}");
            }
        }

        // ================= REGISTRO E GRAVAÇÃO DE LOGS =================
        public static void AdicionarLog(string titulo, bool status)
        {
            string mensagem = $"{titulo}: {(status ? "Ativado" : "Desativado")}";
            Color cor = status ? Colors.Green : Colors.Red;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ListaDeLogs.Insert(0, new EventoLog
                {
                    Titulo = mensagem,
                    Horario = DateTime.Now.ToString("HH:mm:ss"),
                    StatusColor = cor
                });
            });

            _ = SalvarLogNoFirebaseAsync(titulo, mensagem);
        }

        public static void RegistrarEventoExterno(string nomeSensor, string mensagem)
        {
            string detalhe = $"{nomeSensor}: {mensagem}";

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ListaDeLogs.Insert(0, new EventoLog
                {
                    Titulo = detalhe,
                    Horario = DateTime.Now.ToString("HH:mm:ss"),
                    StatusColor = Colors.Yellow
                });
            });

            _ = SalvarLogNoFirebaseAsync(nomeSensor, mensagem);
        }

        // Grava o log na subcoleção "Historico" do dono da casa
        private static async Task SalvarLogNoFirebaseAsync(string tipo, string mensagem)
        {
            try
            {
                string cpfDono = Preferences.Get("CpfDonoCasa", string.Empty);
                if (string.IsNullOrEmpty(cpfDono) || FirestoreDb == null) return;

                DocumentReference historicoRef = FirestoreDb
                    .Collection("Usuarios")
                    .Document(cpfDono)
                    .Collection("Historico")
                    .Document();

                Dictionary<string, object> logData = new Dictionary<string, object>
                {
                    { "tipo", tipo },
                    { "mensagem", mensagem },
                    { "dataHora", FieldValue.ServerTimestamp }
                };

                await historicoRef.SetAsync(logData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao salvar log no Firebase: {ex.Message}");
            }
        }

        // Carrega o histórico salvo no Firestore para exibir na LogsPage
        public static async Task CarregarLogsDoFirebaseAsync()
        {
            try
            {
                string cpfDono = Preferences.Get("CpfDonoCasa", string.Empty);
                if (string.IsNullOrEmpty(cpfDono)) return;

                if (FirestoreDb == null)
                {
                    await InicializarFirebase();
                    if (FirestoreDb == null) return;
                }

                Query query = FirestoreDb
                    .Collection("Usuarios")
                    .Document(cpfDono)
                    .Collection("Historico")
                    .OrderByDescending("dataHora")
                    .Limit(30);

                QuerySnapshot snapshot = await query.GetSnapshotAsync();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ListaDeLogs.Clear();

                    foreach (DocumentSnapshot doc in snapshot.Documents)
                    {
                        if (!doc.Exists) continue;

                        doc.TryGetValue("tipo", out string tipo);
                        doc.TryGetValue("mensagem", out string mensagem);
                        doc.TryGetValue("dataHora", out Timestamp timestamp);

                        DateTime horaLocal = timestamp != null ? timestamp.ToDateTime().ToLocalTime() : DateTime.Now;

                        ListaDeLogs.Add(new EventoLog
                        {
                            Titulo = string.IsNullOrEmpty(tipo) ? mensagem : $"{tipo}: {mensagem}",
                            Horario = horaLocal.ToString("HH:mm:ss"),
                            StatusColor = mensagem != null && mensagem.Contains("DETECTADO") ? Colors.Yellow : Colors.Green
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao carregar logs do Firebase: {ex.Message}");
            }
        }
    }
}
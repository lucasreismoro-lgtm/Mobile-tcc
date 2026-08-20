using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace dfgger
{
    public static class SistemaService
    {
        public static FirestoreDb? FirestoreDb { get; set; }
        public static bool IsFirebaseConectado => FirestoreDb != null;
        public static string CpfUsuarioAtual { get; set; } = string.Empty;

        public static bool PresencaAtivo { get; set; } = false;
        public static bool CalorAtivo { get; set; } = false;
        public static bool AlarmeAtivo { get; set; } = false;

        public static ObservableCollection<EventoLog> ListaDeLogs { get; set; } = new ObservableCollection<EventoLog>();

        public static async Task InicializarFirebase()
        {
            try
            {
                if (FirestoreDb != null) return;

                using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json");
                var credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromStream(stream)
                    .CreateScoped("https://www.googleapis.com/auth/datastore");

                FirestoreDbBuilder builder = new FirestoreDbBuilder
                {
                    ProjectId = "banco-tcc-dc633",
                    Credential = credential
                };

                FirestoreDb = await builder.BuildAsync();
                Debug.WriteLine("[FIREBASE] Conectado com sucesso!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERRO FIREBASE INICIALIZACAO] {ex.Message}");
                FirestoreDb = null;
            }
        }

        public static FirestoreDb ObterInstanciaFirestore()
        {
            return FirestoreDb;
        }

        public static async Task CarregarLogsDoFirebaseAsync()
        {
            try
            {
                if (FirestoreDb == null)
                    await InicializarFirebase();

                if (FirestoreDb == null) return;

                string cpfDono = string.IsNullOrEmpty(CpfUsuarioAtual)
                    ? Preferences.Get("CpfDonoCasa", string.Empty)
                    : CpfUsuarioAtual;

                if (string.IsNullOrEmpty(cpfDono)) return;

                Query query = FirestoreDb.Collection("Usuarios")
                                        .Document(cpfDono)
                                        .Collection("Eventos")
                                        .OrderByDescending("dataHora");

                QuerySnapshot snapshot = await query.GetSnapshotAsync();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ListaDeLogs.Clear();

                    foreach (DocumentSnapshot doc in snapshot.Documents)
                    {
                        if (doc.Exists)
                        {
                            string sensor = doc.ContainsField("sensor") ? doc.GetValue<string>("sensor") : "Sistema";
                            string mensagem = doc.ContainsField("mensagem") ? doc.GetValue<string>("mensagem") : "Evento registrado";

                            DateTime dataHora = DateTime.Now;
                            if (doc.ContainsField("dataHora") && doc.GetValue<Timestamp?>("dataHora").HasValue)
                            {
                                dataHora = doc.GetValue<Timestamp>("dataHora").ToDateTime().ToLocalTime();
                            }

                            Color corStatus = Colors.Gray;
                            if (sensor.Contains("Presença", StringComparison.OrdinalIgnoreCase))
                                corStatus = Colors.Orange;
                            else if (sensor.Contains("Calor", StringComparison.OrdinalIgnoreCase))
                                corStatus = Colors.Red;
                            else if (sensor.Contains("Sistema", StringComparison.OrdinalIgnoreCase) || mensagem.Contains("Ativado", StringComparison.OrdinalIgnoreCase))
                                corStatus = Colors.Green;

                            ListaDeLogs.Add(new EventoLog
                            {
                                Titulo = $"[{sensor}] {mensagem}",
                                Horario = dataHora.ToString("dd/MM/yyyy HH:mm:ss"),
                                StatusColor = corStatus
                            });
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERRO CARREGAR LOGS] {ex.Message}");
            }
        }

        // Método de registrar log local + gravar no Firebase Firestore
        public static async Task RegistrarEventoAsync(string sensor, string mensagem)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ListaDeLogs.Insert(0, new EventoLog
                {
                    Titulo = $"[{sensor}] {mensagem}",
                    Horario = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                    StatusColor = mensagem.Contains("Ativado", StringComparison.OrdinalIgnoreCase) ? Colors.Green : Colors.Gray
                });
            });

            try
            {
                if (FirestoreDb == null) await InicializarFirebase();
                if (FirestoreDb == null) return;

                string cpfDono = string.IsNullOrEmpty(CpfUsuarioAtual)
                    ? Preferences.Get("CpfDonoCasa", string.Empty)
                    : CpfUsuarioAtual;

                if (string.IsNullOrEmpty(cpfDono)) return;

                var novoLog = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "sensor", sensor },
                    { "mensagem", mensagem },
                    { "dataHora", Timestamp.FromDateTime(DateTime.UtcNow) }
                };

                await FirestoreDb.Collection("Usuarios")
                                .Document(cpfDono)
                                .Collection("Eventos")
                                .AddAsync(novoLog);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERRO REGISTRAR EVENTO NO FIREBASE] {ex.Message}");
            }
        }

        public static void AdicionarLog(string componente, bool estado)
        {
            string acao = estado ? "Ativado" : "Desativado";
            string mensagem = $"{componente} foi {acao}.";
            _ = RegistrarEventoAsync(componente, mensagem);
        }

        public static void RegistrarEventoExterno(string sensor, string mensagem)
        {
            _ = RegistrarEventoAsync(sensor, mensagem);
        }
    }
}
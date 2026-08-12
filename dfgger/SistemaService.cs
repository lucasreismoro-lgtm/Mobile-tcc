using System;
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

        // ================= INICIALIZAÇÃO CORRIGIDA DO FIREBASE =================
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

        // ================= REGISTRO DE LOGS =================
        public static void AdicionarLog(string titulo, bool status)
        {
            ListaDeLogs.Insert(0, new EventoLog
            {
                Titulo = $"{titulo}: {(status ? "Ativado" : "Desativado")}",
                Horario = DateTime.Now.ToString("HH:mm:ss"),
                StatusColor = status ? Colors.Green : Colors.Red
            });
        }

        public static void RegistrarEventoExterno(string nomeSensor, string mensagem)
        {
            ListaDeLogs.Insert(0, new EventoLog
            {
                Titulo = $"{nomeSensor}: {mensagem}",
                Horario = DateTime.Now.ToString("HH:mm:ss"),
                StatusColor = Colors.Yellow
            });
        }
    }
}
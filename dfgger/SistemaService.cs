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
        // Propriedade pública para conexão com o Firestore
        public static FirestoreDb? FirestoreDb { get; set; }

        // Estados atuais
        public static bool PresencaAtivo { get; set; } = true;
        public static bool CalorAtivo { get; set; } = true;
        public static bool AlarmeAtivo { get; set; } = true;

        // Flag para controlar se o Firebase está conectado
        public static bool IsFirebaseConectado { get; set; } = false;

        // Lista do Histórico
        public static ObservableCollection<EventoLog> ListaDeLogs { get; set; } = new ObservableCollection<EventoLog>();

        // ================= MÉTODO DE CONEXÃO COM O FIREBASE =================
        public static async Task InicializarFirebase()
        {
            if (FirestoreDb != null) return; // Se já estiver conectado, não faz nada

            try
            {
                // Abre o arquivo conexao.json na pasta Resources/Raw
                using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json");

                // Carrega as credenciais via GoogleCredential (evita erros no Android)
                var credential = GoogleCredential.FromStream(stream);

                // Configura o construtor com o ID do seu projeto
                var builder = new FirestoreDbBuilder
                {
                    ProjectId = "banco-tcc-dc633",
                    Credential = credential
                };

                // Atribui a conexão à propriedade pública FirestoreDb
                FirestoreDb = await builder.BuildAsync();

                // Atualiza o status global para verdadeiro
                IsFirebaseConectado = true;
                System.Diagnostics.Debug.WriteLine("Firebase conectado com sucesso no Mobile!");
            }
            catch (Exception ex)
            {
                IsFirebaseConectado = false;
                System.Diagnostics.Debug.WriteLine($"Erro ao iniciar o Firebase: {ex.Message}");
            }
        }

        // ================= MÉTODO VALIDAR LOGIN PELO CPF =================
        public static async Task<bool> ValidarUsuarioPorCpf(string cpfDigitado)
        {
            // Garante que o Firebase está ativo antes de fazer a busca
            await InicializarFirebase();

            if (FirestoreDb == null)
            {
                System.Diagnostics.Debug.WriteLine("Erro: FirestoreDb não foi inicializado.");
                return false;
            }

            try
            {
                // Busca na coleção "Usuarios" o documento com o CPF
                DocumentReference docRef = FirestoreDb.Collection("Usuarios").Document(cpfDigitado);
                DocumentSnapshot snap = await docRef.GetSnapshotAsync();

                // Se o documento existir no Firebase, retorna true (Acesso Permitido)
                if (snap.Exists)
                {
                    RegistrarEventoExterno("Login", $"Usuário CPF {cpfDigitado} acessou o sistema.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao buscar CPF: {ex.Message}");
            }

            return false; // Se não achar ou der erro, bloqueia o acesso
        }

        // ================= MÉTODOS AUXILIARES E LOGS =================
        public static void AdicionarLog(string titulo, bool status)
        {
            ListaDeLogs.Insert(0, new EventoLog
            {
                Titulo = $"{titulo}: {ObterStatus(status)}",
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

        public static void AtualizarStatusFirebase(bool conectado)
        {
            IsFirebaseConectado = conectado;
        }

        public static string ObterStatus(bool estado) => estado ? "Ativado" : "Desativado";
    }
}
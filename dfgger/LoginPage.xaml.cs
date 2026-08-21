using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Google.Cloud.Firestore;
using Google.Apis.Auth.OAuth2;

namespace dfgger
{
    public partial class LoginPage : ContentPage
    {
        private FirestoreDb? _db;

        public LoginPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await InicializarFirebase();
        }

        private async Task InicializarFirebase()
        {
            if (_db != null) return;

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json");

                // Definição explícita do escopo Datastore que o Android exige
                var credential = GoogleCredential.FromStream(stream)
                    .CreateScoped("https://www.googleapis.com/auth/datastore");

                FirestoreDbBuilder builder = new FirestoreDbBuilder
                {
                    ProjectId = "banco-tcc-dc633",
                    Credential = credential
                };

                _db = await builder.BuildAsync();

                // Sincroniza a instância com o SistemaService
                SistemaService.FirestoreDb = _db;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro de Conexão", "Falha ao conectar com o banco: " + ex.Message, "OK");
            }
        }

        private async Task AtualizarSessaoAtiva(string cpfLogado)
        {
            if (_db == null) return;

            try
            {
                DocumentReference docSessao = _db.Collection("Sessaoativa").Document("SessaoAtiva");

                Dictionary<string, object> dadosSessao = new Dictionary<string, object>
                {
                    { "cpf", cpfLogado }
                };

                // Atualiza o documento SessaoAtiva no Firestore para o ESP8266 identificar o novo usuário
                await docSessao.SetAsync(dadosSessao, SetOptions.MergeAll);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar SessaoAtiva: {ex.Message}");
            }
        }

        private async void BtnEntrar_Clicked(object sender, EventArgs e)
        {
            if (_db == null)
            {
                await DisplayAlert("Aviso", "O banco de dados ainda não foi inicializado.", "OK");
                return;
            }

            string nomeDigitado = TxtNome.Text?.Trim() ?? "";
            string cpfDigitado = Regex.Replace(TxtCpf.Text?.Trim() ?? "", @"[^\d]", "");
            string cepDigitado = Regex.Replace(TxtCep.Text?.Trim() ?? "", @"[^\d]", "");
            string idResidenciaDigitado = TxtIdResidencia.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(nomeDigitado) || string.IsNullOrEmpty(cpfDigitado) ||
                string.IsNullOrEmpty(cepDigitado) || string.IsNullOrEmpty(idResidenciaDigitado))
            {
                await DisplayAlert("Atenção", "Por favor, preencha todos os 4 campos para entrar.", "OK");
                return;
            }

            BtnEntrar.IsEnabled = false;
            IndicadorCarregando.IsVisible = true;
            IndicadorCarregando.IsRunning = true;

            try
            {
                string nomeEncontrado = "";
                string cargoEncontrado = "";
                string cepEncontrado = "";
                string idResEncontrado = "";
                string cpfDonoCasa = "";
                bool usuarioAchei = false;

                // 1º PASSO: Tenta buscar na raiz (Dono da Casa pelo CPF)
                DocumentReference docDonoRef = _db.Collection("Usuarios").Document(cpfDigitado);
                DocumentSnapshot snapDono = await docDonoRef.GetSnapshotAsync();

                if (snapDono.Exists)
                {
                    snapDono.TryGetValue("nome", out nomeEncontrado);
                    snapDono.TryGetValue("cargo", out cargoEncontrado);
                    snapDono.TryGetValue("cep", out cepEncontrado);
                    snapDono.TryGetValue("id_residencia", out idResEncontrado);

                    cpfDonoCasa = cpfDigitado;
                    usuarioAchei = true;
                }
                else // 2º PASSO: Procura dentro da subcoleção Moradores
                {
                    QuerySnapshot todasCasas = await _db.Collection("Usuarios").GetSnapshotAsync();

                    foreach (DocumentSnapshot casaDoc in todasCasas.Documents)
                    {
                        DocumentReference docMoradorRef = casaDoc.Reference.Collection("Moradores").Document(cpfDigitado);
                        DocumentSnapshot snapMorador = await docMoradorRef.GetSnapshotAsync();

                        if (snapMorador.Exists)
                        {
                            snapMorador.TryGetValue("nome", out nomeEncontrado);
                            snapMorador.TryGetValue("cargo", out cargoEncontrado);

                            casaDoc.TryGetValue("cep", out cepEncontrado);
                            casaDoc.TryGetValue("id_residencia", out idResEncontrado);

                            cpfDonoCasa = casaDoc.Id;
                            usuarioAchei = true;
                            break;
                        }
                    }
                }

                if (!usuarioAchei)
                {
                    await DisplayAlert("Acesso Negado", "Usuário não encontrado. Verifique o CPF digitado.", "OK");
                    return;
                }

                string cepBancoLimpo = Regex.Replace(cepEncontrado ?? "", @"[^\d]", "");

                bool nomeValido = string.Equals(nomeDigitado, nomeEncontrado, StringComparison.OrdinalIgnoreCase);
                bool cepValido = string.Equals(cepDigitado, cepBancoLimpo);
                bool idResValido = string.Equals(idResidenciaDigitado, idResEncontrado, StringComparison.OrdinalIgnoreCase);

                if (nomeValido && cepValido && idResValido)
                {
                    // Atualiza a sessão ativa no Firestore
                    await AtualizarSessaoAtiva(cpfDonoCasa);

                    Preferences.Set("CpfDonoCasa", cpfDonoCasa);
                    Application.Current.MainPage = new NavigationPage(new DashboardPage(nomeEncontrado, cargoEncontrado));
                }
                else
                {
                    await DisplayAlert("Acesso Negado", "Dados incorretos. Verifique Nome, CEP e ID da Residência.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro", "Erro ao autenticar: " + ex.Message, "OK");
            }
            finally
            {
                BtnEntrar.IsEnabled = true;
                IndicadorCarregando.IsRunning = false;
                IndicadorCarregando.IsVisible = false;
            }
        }
    }
}
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
        private FirestoreDb? _db; // Referência privada para a instância do banco Firestore

        public LoginPage()
        {
            InitializeComponent(); // Carrega e inicializa os componentes declarados no XAML
        }

        protected override async void OnAppearing() // Método acionado quando a tela é exibida
        {
            base.OnAppearing(); // Executa o comportamento original da classe base
            await InicializarFirebase(); // Chama a inicialização assíncrona do Firebase
        }

        private async Task InicializarFirebase() // Método assíncrono para conectar com o Firestore
        {
            if (_db != null) return; // Se a conexão já existir, encerra o método

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json"); // Abre o arquivo de chave do Firebase no app

                var credential = GoogleCredential.FromStream(stream) // Carrega as credenciais do arquivo JSON
                    .CreateScoped("https://www.googleapis.com/auth/datastore"); // Aplica o escopo de acesso ao Datastore/Firestore

                FirestoreDbBuilder builder = new FirestoreDbBuilder // Instancia o construtor do cliente Firestore
                {
                    ProjectId = "banco-tcc-dc633", // Define o ID do projeto cadastrado no Firebase
                    Credential = credential // Atribui a credencial criada
                };

                _db = await builder.BuildAsync(); // Constrói a conexão com o banco assincronamente

                SistemaService.FirestoreDb = _db; // Compartilha a conexão com a classe global do serviço
            }
            catch (Exception ex) // Captura falhas na inicialização
            {
                await DisplayAlert("Erro de Conexão", "Falha ao conectar com o banco: " + ex.Message, "OK"); // Exibe mensagem do erro
            }
        }

        private async Task AtualizarSessaoAtiva(string cpfLogado) // Método para registrar o usuário ativo na sessão
        {
            if (_db == null) return; // Cancela se não houver conexão com o banco

            try
            {
                DocumentReference docSessao = _db.Collection("Sessaoativa").Document("SessaoAtiva"); // Aponta para o documento da sessão

                Dictionary<string, object> dadosSessao = new Dictionary<string, object>
                {
                    { "cpf", cpfLogado } // Adiciona o CPF do usuário autenticado
                };

                await docSessao.SetAsync(dadosSessao, SetOptions.MergeAll); // Salva o CPF mesclando com o documento existente
            }
            catch (Exception ex) // Captura exceções ao atualizar sessão
            {
                Console.WriteLine($"Erro ao atualizar SessaoAtiva: {ex.Message}"); // Registra a falha no console
            }
        }

        private async void BtnEntrar_Clicked(object sender, EventArgs e) // Evento disparado ao clicar em "Entrar"
        {
            if (_db == null)
            {
                await DisplayAlert("Aviso", "O banco de dados ainda não foi inicializado.", "OK"); // Alerta que o banco não está pronto
                return; // Cancela a tentativa de login
            }

            string nomeDigitado = TxtNome.Text?.Trim() ?? ""; // Obtém o nome digitado sem espaços extras nas pontas
            string cpfDigitado = Regex.Replace(TxtCpf.Text?.Trim() ?? "", @"[^\d]", ""); // Obtém o CPF e remove caracteres não numéricos
            string cepDigitado = Regex.Replace(TxtCep.Text?.Trim() ?? "", @"[^\d]", ""); // Obtém o CEP e remove caracteres não numéricos
            string idResidenciaDigitado = TxtIdResidencia.Text?.Trim() ?? ""; // Obtém o ID da residência digitado

            if (string.IsNullOrEmpty(nomeDigitado) || string.IsNullOrEmpty(cpfDigitado) ||
                string.IsNullOrEmpty(cepDigitado) || string.IsNullOrEmpty(idResidenciaDigitado)) // Valida se todos os campos foram preenchidos
            {
                await DisplayAlert("Atenção", "Por favor, preencha todos os 4 campos para entrar.", "OK"); // Exibe alerta pedindo preenchimento total
                return; // Interrompe a execução caso falte algum dado
            }

            BtnEntrar.IsEnabled = false; // Desabilita o botão para evitar cliques múltiplos
            IndicadorCarregando.IsVisible = true; // Exibe o indicador visual de carregamento
            IndicadorCarregando.IsRunning = true; // Ativa a animação do carregamento

            try
            {
                string nomeEncontrado = ""; // Variavel auxiliar para o nome retornado do banco
                string cargoEncontrado = ""; // Variavel auxiliar para o cargo retornado do banco
                string cepEncontrado = ""; // Variavel auxiliar para o CEP retornado do banco
                string idResEncontrado = ""; // Variavel auxiliar para o ID da residencia do banco
                string cpfDonoCasa = ""; // Variavel auxiliar para o CPF do proprietário
                bool usuarioAchei = false; // Flag de controle indicando se o usuário foi localizado

                DocumentReference docDonoRef = _db.Collection("Usuarios").Document(cpfDigitado); // Aponta a busca no documento do Dono pelo CPF
                DocumentSnapshot snapDono = await docDonoRef.GetSnapshotAsync(); // Obtém os dados do Dono no Firestore

                if (snapDono.Exists) // Se o CPF pertencer a um Dono cadastrado na raiz...
                {
                    snapDono.TryGetValue("nome", out nomeEncontrado); // Extrai o nome do documento
                    snapDono.TryGetValue("cargo", out cargoEncontrado); // Extrai o cargo do documento
                    snapDono.TryGetValue("cep", out cepEncontrado); // Extrai o CEP do documento
                    snapDono.TryGetValue("id_residencia", out idResEncontrado); // Extrai o ID da residência

                    cpfDonoCasa = cpfDigitado; // Define o próprio CPF como sendo o do Dono da casa
                    usuarioAchei = true; // Marca que o usuário foi localizado
                }
                else // Caso não esteja na raiz, procura dentro da subcoleção Moradores...
                {
                    QuerySnapshot todasCasas = await _db.Collection("Usuarios").GetSnapshotAsync(); // Busca todas as casas cadastradas

                    foreach (DocumentSnapshot casaDoc in todasCasas.Documents) // Percorre cada casa cadastrada
                    {
                        DocumentReference docMoradorRef = casaDoc.Reference.Collection("Moradores").Document(cpfDigitado); // Aponta para a subcoleção "Moradores"
                        DocumentSnapshot snapMorador = await docMoradorRef.GetSnapshotAsync(); // Obtém o documento do Morador

                        if (snapMorador.Exists) // Se o registro do Morador existir nesta casa...
                        {
                            snapMorador.TryGetValue("nome", out nomeEncontrado); // Extrai o nome do Morador
                            snapMorador.TryGetValue("cargo", out cargoEncontrado); // Extrai o cargo do Morador

                            casaDoc.TryGetValue("cep", out cepEncontrado); // Extrai o CEP do documento pai (da casa)
                            casaDoc.TryGetValue("id_residencia", out idResEncontrado); // Extrai o ID da residência da casa pai

                            cpfDonoCasa = casaDoc.Id; // Armazena o CPF do Dono (ID da casa pai)
                            usuarioAchei = true; // Marca que o usuário foi localizado
                            break; // Encerra o loop de busca entre as casas
                        }
                    }
                }

                if (!usuarioAchei) // Caso o CPF não pertença a nenhum Dono ou Morador
                {
                    await DisplayAlert("Acesso Negado", "Usuário não encontrado. Verifique o CPF digitado.", "OK"); // Exibe notificação de erro
                    return; // Cancela o processo de login
                }

                string cepBancoLimpo = Regex.Replace(cepEncontrado ?? "", @"[^\d]", ""); // Limpa qualquer formatação do CEP retornado do banco

                bool nomeValido = string.Equals(nomeDigitado, nomeEncontrado, StringComparison.OrdinalIgnoreCase); // Compara o nome ignorando maiúsculas/minúsculas
                bool cepValido = string.Equals(cepDigitado, cepBancoLimpo); // Compara se os números do CEP conferem
                bool idResValido = string.Equals(idResidenciaDigitado, idResEncontrado, StringComparison.OrdinalIgnoreCase); // Compara se o ID da Residência confere

                if (nomeValido && cepValido && idResValido) // Se as 3 validações forem corretas...
                {
                    await AtualizarSessaoAtiva(cpfDonoCasa); // Notifica o banco e o hardware sobre o novo login

                    Preferences.Set("CpfDonoCasa", cpfDonoCasa); // Armazena o CPF do Dono localmente no aparelho
                    Application.Current.MainPage = new NavigationPage(new DashboardPage(nomeEncontrado, cargoEncontrado)); // Redireciona para a DashboardPage
                }
                else // Se algum dado não coincidir...
                {
                    await DisplayAlert("Acesso Negado", "Dados incorretos. Verifique Nome, CEP e ID da Residência.", "OK"); // Alerta sobre divergência de dados
                }
            }
            catch (Exception ex) // Captura falhas na autenticação ou busca
            {
                await DisplayAlert("Erro", "Erro ao autenticar: " + ex.Message, "OK"); // Exibe mensagem do erro
            }
            finally // Bloco de finalização obrigatório
            {
                BtnEntrar.IsEnabled = true; // Reabilita o botão para novos cliques
                IndicadorCarregando.IsRunning = false; // Para a animação de carregamento
                IndicadorCarregando.IsVisible = false; // Oculta o indicador de carregamento
            }
        }
    }
}
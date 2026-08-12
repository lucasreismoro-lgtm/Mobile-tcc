using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Google.Cloud.Firestore;

namespace dfgger
{
    public partial class LoginPage : ContentPage // Classe parcial associada à tela XAML de login
    {
        private FirestoreDb? _db; // Guarda a instância da conexão com o Firestore

        public LoginPage() // Construtor padrão da tela de Login
        {
            InitializeComponent(); // Carrega e desenha os componentes visuais do XAML
        }

        protected override async void OnAppearing() // Executado automaticamente ao abrir a página
        {
            base.OnAppearing(); // Mantém o ciclo de vida nativo de abertura da tela no .NET MAUI
            await InicializarFirebase(); // Executa o método assíncrono para conectar ao banco
        }

        private async Task InicializarFirebase() // Método assíncrono para conectar com o Firebase
        {
            if (_db != null) return; // Se a conexão já existir, não reconecta

            try // Bloco de proteção contra erros de conexão
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json"); // Abre o arquivo de credenciais do app
                using var reader = new StreamReader(stream); // Leitor para processar o arquivo
                string jsonConteudo = await reader.ReadToEndAsync(); // Lê o JSON de credenciais para string

                FirestoreDbBuilder builder = new FirestoreDbBuilder // Construtor de configurações do Firestore
                {
                    ProjectId = "banco-tcc-dc633", // Define a chave identificadora do projeto no Firebase
                    JsonCredentials = jsonConteudo // Injeta as credenciais para autenticação
                };
                _db = builder.Build(); // Conclui e salva a instância ativa do banco
            }
            catch (Exception ex) // Captura exceções de arquivo ou rede
            {
                await DisplayAlert("Erro de Conexão", "Falha ao conectar com o banco: " + ex.Message, "OK"); // Exibe pop-up de erro
            }
        }

        private async void BtnEntrar_Clicked(object sender, EventArgs e) // Disparado ao clicar no botão Entrar
        {
            if (_db == null) // Checa se o banco de dados está pronto
            {
                await DisplayAlert("Aviso", "O banco de dados ainda não foi inicializado.", "OK"); // Pede para o usuário aguardar
                return; // Cancela a execução
            }

            string nomeDigitado = TxtNome.Text?.Trim() ?? ""; // Captura o nome e remove espaços nas pontas
            string cpfDigitado = Regex.Replace(TxtCpf.Text?.Trim() ?? "", @"[^\d]", ""); // Filtra e deixa apenas números no CPF
            string cepDigitado = Regex.Replace(TxtCep.Text?.Trim() ?? "", @"[^\d]", ""); // Filtra e deixa apenas números no CEP
            string idResidenciaDigitado = TxtIdResidencia.Text?.Trim() ?? ""; // Captura o ID da residência limpo

            if (string.IsNullOrEmpty(nomeDigitado) || string.IsNullOrEmpty(cpfDigitado) || // Valida se Nome ou CPF estão em branco
                string.IsNullOrEmpty(cepDigitado) || string.IsNullOrEmpty(idResidenciaDigitado)) // Valida se CEP ou ID estão em branco
            {
                await DisplayAlert("Atenção", "Por favor, preencha todos os 4 campos para entrar.", "OK"); // Avisa dados faltantes
                return; // Interrompe o processo
            }

            BtnEntrar.IsEnabled = false; // Desativa o botão para evitar cliques duplos
            IndicadorCarregando.IsVisible = true; // Mostra o carregador na tela
            IndicadorCarregando.IsRunning = true; // Inicia a animação do carregador

            try // Bloco de execução das consultas no Firestore
            {
                string nomeEncontrado = ""; // Nome retornado do Firestore
                string cargoEncontrado = ""; // Cargo retornado do Firestore
                string cepEncontrado = ""; // CEP retornado do Firestore
                string idResEncontrado = ""; // ID da residência retornado do Firestore
                string cpfDonoCasa = ""; // CPF do responsável pela casa
                bool usuarioAchei = false; // Flag para indicar se o usuário foi achado

                // 1º PASSO: Tenta buscar na raiz (Dono da Casa pelo CPF)
                DocumentReference docDonoRef = _db.Collection("Usuarios").Document(cpfDigitado); // Referência ao documento do CPF
                DocumentSnapshot snapDono = await docDonoRef.GetSnapshotAsync(); // Busca o documento no banco

                if (snapDono.Exists) // Caso o CPF seja do Dono da Casa
                {
                    snapDono.TryGetValue("nome", out nomeEncontrado); // Extrai o nome
                    snapDono.TryGetValue("cargo", out cargoEncontrado); // Extrai o cargo
                    snapDono.TryGetValue("cep", out cepEncontrado); // Extrai o CEP
                    snapDono.TryGetValue("id_residencia", out idResEncontrado); // Extrai o ID da residência

                    cpfDonoCasa = cpfDigitado; // Grava que o usuário logado é o próprio dono
                    usuarioAchei = true; // Marca que o usuário foi localizado
                }
                else // 2º PASSO: Procura dentro da subcoleção Moradores
                {
                    QuerySnapshot todasCasas = await _db.Collection("Usuarios").GetSnapshotAsync(); // Busca todas as residências

                    foreach (DocumentSnapshot casaDoc in todasCasas.Documents) // Percorre cada casa cadastrada
                    {
                        DocumentReference docMoradorRef = casaDoc.Reference.Collection("Moradores").Document(cpfDigitado); // Aponta para a subcoleção do morador
                        DocumentSnapshot snapMorador = await docMoradorRef.GetSnapshotAsync(); // Tenta ler o documento do morador

                        if (snapMorador.Exists) // Se encontrou o morador na subcoleção
                        {
                            snapMorador.TryGetValue("nome", out nomeEncontrado); // Extrai nome do morador
                            snapMorador.TryGetValue("cargo", out cargoEncontrado); // Extrai cargo do morador

                            casaDoc.TryGetValue("cep", out cepEncontrado); // Herda o CEP da casa do Dono
                            casaDoc.TryGetValue("id_residencia", out idResEncontrado); // Herda o ID da Residência do Dono

                            cpfDonoCasa = casaDoc.Id; // Salva o CPF/ID do Dono responsável pela casa
                            usuarioAchei = true; // Marca que o morador foi localizado
                            break; // Sai do laço de repetição
                        }
                    }
                }

                if (!usuarioAchei) // Se o CPF não for encontrado no banco
                {
                    await DisplayAlert("Acesso Negado", "Usuário não encontrado. Verifique o CPF digitado.", "OK"); // Avisa CPF inválido
                    return; // Cancela autenticação
                }

                string cepBancoLimpo = Regex.Replace(cepEncontrado ?? "", @"[^\d]", ""); // Limpa caracteres especiais do CEP do banco

                bool nomeValido = string.Equals(nomeDigitado, nomeEncontrado, StringComparison.OrdinalIgnoreCase); // Valida nome ignorando maiúsculas/minúsculas
                bool cepValido = string.Equals(cepDigitado, cepBancoLimpo); // Valida o CEP digitado
                bool idResValido = string.Equals(idResidenciaDigitado, idResEncontrado, StringComparison.OrdinalIgnoreCase); // Valida o ID da residência digitado

                if (nomeValido && cepValido && idResValido) // Se todas as confirmações baterem
                {
                    Preferences.Set("CpfDonoCasa", cpfDonoCasa); // Guarda o CPF do Dono na memória local do app para usar no sistema

                    Application.Current.MainPage = new NavigationPage(new DashboardPage(nomeEncontrado, cargoEncontrado)); // Abre a Dashboard
                }
                else // Se algum dado não bater
                {
                    await DisplayAlert("Acesso Negado", "Dados incorretos. Verifique Nome, CEP e ID da Residência.", "OK"); // Alerta erro
                }
            }
            catch (Exception ex) // Captura falhas de execução
            {
                await DisplayAlert("Erro", "Erro ao autenticar: " + ex.Message, "OK"); // Exibe mensagem do erro
            }
            finally // Executado obrigatoriamente no final
            {
                BtnEntrar.IsEnabled = true; // Reativa o botão
                IndicadorCarregando.IsRunning = false; // Desliga a animação do carregador
                IndicadorCarregando.IsVisible = false; // Esconde o carregador
            }
        }
    }
}
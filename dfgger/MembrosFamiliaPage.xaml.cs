using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Google.Cloud.Firestore;

namespace dfgger
{
    public class MoradorModel // Modelo de dados para representar qualquer membro da residência
    {
        public string Nome { get; set; } = ""; // Armazena o campo 'nome'
        public string Email { get; set; } = ""; // Armazena o campo 'email'
        public string Cargo { get; set; } = ""; // Armazena o campo 'cargo'
        public string Cpf { get; set; } = ""; // Armazena o campo 'cpf'

        public string CargoExibicao => string.IsNullOrWhiteSpace(Cargo) // Formatação da badge de cargo
            ? "MORADOR" // Padrão se estiver em branco
            : Cargo.Replace("_", " ").ToUpper(); // Converte 'dono_da_casa' para 'DONO DA CASA'
    }

    public partial class MembrosFamiliaPage : ContentPage // Lógica da tela de membros da família
    {
        private FirestoreDb? _db; // Mantém a conexão ativa com o Firestore

        public MembrosFamiliaPage() // Construtor padrão
        {
            InitializeComponent(); // Carrega os elementos visuais do XAML
        }

        protected override async void OnAppearing() // Disparado automaticamente ao abrir a tela
        {
            base.OnAppearing(); // Mantém o ciclo de vida padrão do MAUI
            await InicializarEBuscarMembrosAsync(); // Inicia a consulta no banco
        }

        private async Task InicializarEBuscarMembrosAsync() // Método principal de carregamento
        {
            LoadingIndicator.IsVisible = true; // Mostra o carregador na tela
            LoadingIndicator.IsRunning = true; // Ativa a animação do carregador
            CvMembros.IsVisible = false; // Esconde a lista temporariamente
            LblSemMembros.IsVisible = false; // Esconde a mensagem de aviso

            try // Bloco de consulta ao banco de dados
            {
                if (_db == null) // Se o banco ainda não estiver conectado
                {
                    using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json"); // Carrega o JSON de credenciais
                    using var reader = new StreamReader(stream); // Prepara o leitor
                    string jsonConteudo = await reader.ReadToEndAsync(); // Lê as credenciais para string

                    FirestoreDbBuilder builder = new FirestoreDbBuilder // Configura a conexão
                    {
                        ProjectId = "banco-tcc-dc633", // ID do projeto Firebase
                        JsonCredentials = jsonConteudo // Adiciona as credenciais
                    };
                    _db = builder.Build(); // Conecta no banco
                }

                string cpfDonoCasa = Preferences.Get("CpfDonoCasa", string.Empty); // Recupera o CPF do Dono salvo no login

                if (string.IsNullOrEmpty(cpfDonoCasa)) // Se a sessão estiver vazia
                {
                    LblSemMembros.Text = "Sessão não encontrada. Por favor, faça login novamente."; // Mensagem de alerta
                    LblSemMembros.IsVisible = true; // Exibe o aviso
                    return; // Cancela a busca
                }

                var listaTodosMembros = new List<MoradorModel>(); // Lista geral para guardar o Dono + Moradores

                // =========================================================================
                // 1º PASSO: BUSCA O DONO DA CASA (Documento na raiz 'Usuarios / {cpfDono}')
                // =========================================================================
                DocumentReference donoRef = _db.Collection("Usuarios").Document(cpfDonoCasa); // Aponta para o documento do Dono
                DocumentSnapshot snapDono = await donoRef.GetSnapshotAsync(); // Busca os dados do Dono no Firestore

                if (snapDono.Exists) // Se o documento do Dono existir
                {
                    snapDono.TryGetValue("nome", out string nomeDono); // Extrai o nome do Dono
                    snapDono.TryGetValue("email", out string emailDono); // Extrai o email do Dono
                    snapDono.TryGetValue("cargo", out string cargoDono); // Extrai o cargo ('dono_da_casa')
                    snapDono.TryGetValue("cpf", out string cpfDono); // Extrai o CPF do Dono

                    listaTodosMembros.Add(new MoradorModel // Adiciona o Dono em primeiro lugar na lista
                    {
                        Nome = nomeDono ?? "",
                        Email = emailDono ?? "",
                        Cargo = cargoDono ?? "dono_da_casa",
                        Cpf = cpfDono ?? cpfDonoCasa
                    });
                }

                // =========================================================================
                // 2º PASSO: BUSCA OS MORADORES (Subcoleção 'Usuarios / {cpfDono} / Moradores')
                // =========================================================================
                CollectionReference moradoresRef = donoRef.Collection("Moradores"); // Rota da subcoleção de moradores
                QuerySnapshot snapshotMoradores = await moradoresRef.GetSnapshotAsync(); // Busca todos os moradores

                foreach (DocumentSnapshot doc in snapshotMoradores.Documents) // Percorre cada morador encontrado
                {
                    if (doc.Exists) // Se o documento do morador for válido
                    {
                        doc.TryGetValue("nome", out string nome); // Extrai 'nome'
                        doc.TryGetValue("email", out string email); // Extrai 'email'
                        doc.TryGetValue("cargo", out string cargo); // Extrai 'cargo'
                        doc.TryGetValue("cpf", out string cpf); // Extrai 'cpf'

                        listaTodosMembros.Add(new MoradorModel // Adiciona o morador na lista
                        {
                            Nome = nome ?? "",
                            Email = email ?? "",
                            Cargo = cargo ?? "morador",
                            Cpf = cpf ?? ""
                        });
                    }
                }

                // =========================================================================
                // 3º PASSO: EXIBE A LISTA COMPLETA NA INTERFACE
                // =========================================================================
                if (listaTodosMembros.Count > 0) // Se encontrou pelo menos um membro
                {
                    CvMembros.ItemsSource = listaTodosMembros; // Envia a lista completa (Dono + Moradores) para o XAML
                    CvMembros.IsVisible = true; // Mostra a lista na tela
                }
                else // Se não encontrar ninguém
                {
                    LblSemMembros.Text = "Nenhum membro encontrado nesta residência."; // Mensagem de aviso
                    LblSemMembros.IsVisible = true; // Exibe o aviso
                }
            }
            catch (Exception ex) // Tratamento de falhas de rede/banco
            {
                System.Diagnostics.Debug.WriteLine($"Erro no Firestore: {ex.Message}"); // Grava o erro no Output
                LblSemMembros.Text = "Erro ao carregar dados do banco de dados."; // Mensagem amigável
                LblSemMembros.IsVisible = true; // Exibe erro na tela
            }
            finally // Executado obrigatoriamente ao final
            {
                LoadingIndicator.IsRunning = false; // Desliga a animação do carregador
                LoadingIndicator.IsVisible = false; // Esconde o carregador
            }
        }
        private async void BtnAdicionarMorador_Clicked(object sender, EventArgs e)
        {
            await DisplayAlert(
                "Adicionar Morador",
                "Contate o provedor do seu serviço para adicionar moradores.",
                "OK"
            );
        }
    }
}
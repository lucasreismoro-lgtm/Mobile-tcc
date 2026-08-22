using System;
using Google.Cloud.Firestore; 
using Microsoft.Maui.ApplicationModel; 
using Microsoft.Maui.Controls; 

namespace dfgger // Declaração do namespace do projeto
{
    public class SensorAlertService // Classe responsável por monitorar o banco e disparar alertas visuais em tempo real
    {
        private FirestoreDb? _db; // Instância privada da conexão com o banco de dados
        private FirestoreChangeListener? _listener; // Objeto escutador que se mantém conectado ao Firestore aguardando novidades
        private bool _primeiraCargaRealizada = false; // Flag de controle (trava) para ignorar os dados já existentes na carga inicial

        // Método que ativa o escutador em tempo real para a coleção de eventos do usuário
        public void IniciarEscutaEventos(string cpfDono, FirestoreDb db)
        {
            PararEscuta(); // Garante que qualquer escuta ativa anterior seja cancelada antes de iniciar uma nova

            _db = db; // Armazena a referência local da conexão com o banco
            if (_db == null || string.IsNullOrEmpty(cpfDono)) return; // Aborta a execução se não houver conexão válida ou CPF fornecido

            // Reseta a trava ao iniciar uma nova sessão de escuta
            _primeiraCargaRealizada = false;

            // Define a referência da subcoleção "Eventos" dentro do nó do usuário no Firestore
            CollectionReference eventosRef = _db.Collection("Usuarios").Document(cpfDono).Collection("Eventos");

            // Registra a escuta em tempo real (push) para qualquer alteração na coleção especificada
            _listener = eventosRef.Listen(snapshot =>
            {
                // Se for a primeira vez que o listener se conecta à coleção, 
                // ele traz todos os eventos antigos do banco. Ignoramos essa primeira leitura!
                if (!_primeiraCargaRealizada)
                {
                    _primeiraCargaRealizada = true; // Marca a carga inicial como concluída
                    return; // Encerra a execução do bloco sem processar os dados históricos antigos
                }

                foreach (DocumentChange change in snapshot.Changes) // Percorre apenas os documentos que foram alterados ou adicionados
                {
                    // Dispara a notificação APENAS para novos documentos adicionados EM TEMPO REAL após a carga inicial
                    if (change.ChangeType == DocumentChange.Type.Added)
                    {
                        var doc = change.Document; // Obtém o documento recém-inserido
                        if (doc.Exists) // Confirma se o documento é válido
                        {
                            // Extrai o nome do sensor (usa "Sensor" se o campo estiver ausente)
                            string sensor = doc.ContainsField("sensor") ? doc.GetValue<string>("sensor") : "Sensor";
                            // Extrai a mensagem de alerta (usa valor padrão se não informado)
                            string mensagem = doc.ContainsField("mensagem") ? doc.GetValue<string>("mensagem") : "Alerta registrado!";

                            // Garante que a exibição da caixa de diálogo aconteça na Thread Principal da Interface do Usuário
                            MainThread.BeginInvokeOnMainThread(async () =>
                            {
                                if (Application.Current?.MainPage != null) // Garante que a tela principal esteja carregada e ativa
                                {
                                    // Exibe um pop-up de alerta na tela do aplicativo com os dados do evento
                                    await Application.Current.MainPage.DisplayAlert($"ALERTA: {sensor}", mensagem, "OK");
                                }
                            });
                        }
                    }
                }
            });
        }

        // Encerra e limpa a escuta ativa para liberar memória e conexões de rede
        public void PararEscuta()
        {
            if (_listener != null) // Se houver um escutador em execução...
            {
                _listener.StopAsync(); // Interrompe o ouvinte do Firestore assincronamente
                _listener = null; // Zera a referência para coleta de lixo
            }
            _primeiraCargaRealizada = false; // Reseta a trava para futuros acionamentos
        }

        // Método utilitário estático para disparar e salvar um alerta de sensor
        public static async System.Threading.Tasks.Task DispararAlertaSensorAsync(string sensor, string local)
        {
            string mensagem = $"Alerta disparado pelo {sensor} no local: {local}"; // Monta o texto do evento
            await SistemaService.RegistrarEventoAsync(sensor, mensagem); // Envia o registro para ser persistido no Firestore
        }
    }
}
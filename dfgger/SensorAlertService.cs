using System;
using Google.Cloud.Firestore;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace dfgger
{
    public class SensorAlertService
    {
        private FirestoreDb? _db;
        private FirestoreChangeListener? _listener;
        private bool _primeiraCargaRealizada = false;

        public void IniciarEscutaEventos(string cpfDono, FirestoreDb db)
        {
            PararEscuta();

            _db = db;
            if (_db == null || string.IsNullOrEmpty(cpfDono)) return;

            // Reseta a trava ao iniciar a escuta
            _primeiraCargaRealizada = false;

            CollectionReference eventosRef = _db.Collection("Usuarios").Document(cpfDono).Collection("Eventos");

            _listener = eventosRef.Listen(snapshot =>
            {
                // Se for a primeira vez que o listener se conecta à coleção, 
                // ele traz todos os eventos antigos do banco. Ignoramos essa primeira leitura!
                if (!_primeiraCargaRealizada)
                {
                    _primeiraCargaRealizada = true;
                    return;
                }

                foreach (DocumentChange change in snapshot.Changes)
                {
                    // Dispara a notificação APENAS para novos documentos adicionados EM TEMPO REAL após a carga inicial
                    if (change.ChangeType == DocumentChange.Type.Added)
                    {
                        var doc = change.Document;
                        if (doc.Exists)
                        {
                            string sensor = doc.ContainsField("sensor") ? doc.GetValue<string>("sensor") : "Sensor";
                            string mensagem = doc.ContainsField("mensagem") ? doc.GetValue<string>("mensagem") : "Alerta registrado!";

                            MainThread.BeginInvokeOnMainThread(async () =>
                            {
                                if (Application.Current?.MainPage != null)
                                {
                                    await Application.Current.MainPage.DisplayAlert($"🚨 ALERTA: {sensor}", mensagem, "OK");
                                }
                            });
                        }
                    }
                }
            });
        }

        public void PararEscuta()
        {
            if (_listener != null)
            {
                _listener.StopAsync();
                _listener = null;
            }
            _primeiraCargaRealizada = false;
        }

        public static async System.Threading.Tasks.Task DispararAlertaSensorAsync(string sensor, string local)
        {
            string mensagem = $"Alerta disparado pelo {sensor} no local: {local}";
            await SistemaService.RegistrarEventoAsync(sensor, mensagem);
        }
    }
}
using Google.Cloud.Firestore;

namespace dfgger
{
    [FirestoreData]
    public class EventoModel
    {
        [FirestoreProperty("sensor")]
        public string Sensor { get; set; }

        [FirestoreProperty("local")]
        public string Local { get; set; }

        [FirestoreProperty("mensagem")]
        public string Mensagem { get; set; }

        [FirestoreProperty("dataHora")]
        public DateTime DataHora { get; set; }
    }
}
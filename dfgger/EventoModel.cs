using Google.Cloud.Firestore; 

namespace dfgger 
{ // duh
    [FirestoreData] // Atributo que marca a classe para ser serializada/deserializada automaticamente pelo Firestore
    public class EventoModel
    { // duh
        [FirestoreProperty("sensor")] // Mapeia a propriedade para o campo exato "sensor" no documento do Firestore
        public string Sensor { get; set; } // Propriedade para armazenar o nome ou tipo do sensor

        [FirestoreProperty("local")] // Mapeia a propriedade para o campo exato "local" no documento do Firestore
        public string Local { get; set; } // Propriedade para armazenar o local onde o evento ocorreu

        [FirestoreProperty("mensagem")] // Mapeia a propriedade para o campo exato "mensagem" no documento do Firestore
        public string Mensagem { get; set; } // Propriedade para armazenar a descrição ou mensagem do evento

        [FirestoreProperty("dataHora")] // Mapeia a propriedade para o campo exato "dataHora" no documento do Firestore
        public DateTime DataHora { get; set; } // Propriedade para armazenar a data e hora do evento
    } // duh
} // duh
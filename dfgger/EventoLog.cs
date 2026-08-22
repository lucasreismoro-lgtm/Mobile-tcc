using Microsoft.Maui.Graphics;

namespace dfgger
{
    public class EventoLog 
    {
        public string Titulo { get; set; } = string.Empty; // Propriedade para o título do log (ex: [Sensor] Mensagem)
        public string Horario { get; set; } = string.Empty; // Propriedade para a data e hora formatadas em texto
        public Color StatusColor { get; set; } = Colors.Gray; // Propriedade de cor para a tag visual de status (padrão cinza)
    }
}
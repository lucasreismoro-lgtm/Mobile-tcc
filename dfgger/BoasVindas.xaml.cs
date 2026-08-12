using Microsoft.Maui.Controls;

namespace dfgger;

public partial class BoasVindas : ContentPage
{
    public BoasVindas()
    {
        InitializeComponent();
    }

    private async void OnComecarClicked(object sender, EventArgs e)
    {
        // Altere para a nova tela de login
        await Navigation.PushAsync(new LoginPage());
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // O escudo já sobe, e agora o título e a frase surgem logo depois
        EscudoContainer.Opacity = 0;
        await EscudoContainer.FadeTo(1, 1000);
    }
}
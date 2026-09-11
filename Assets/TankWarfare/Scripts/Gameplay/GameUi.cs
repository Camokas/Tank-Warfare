using UnityEngine;
using UnityEngine.UI;

namespace TankWarfare.Gameplay
{
    public sealed class GameUi : MonoBehaviour
    {
        public GameObject menuPanel;
        public GameObject connectionPanel;
        public GameObject lobbyPanel;
        public GameObject hudPanel;
        public GameObject resultPanel;
        public GameObject statisticsPanel;

        public InputField nicknameInput;
        public InputField roomInput;
        public InputField serverInput;
        public Button createButton;
        public Button joinButton;
        public Button statisticsButton;
        public Button exitButton;
        public Button heavyButton;
        public Button mediumButton;
        public Button lightButton;
        public Text tankInfoText;
        public Text menuStatusText;

        public Text connectionStatusText;
        public Button cancelButton;
        public Text roomAddressText;
        public Button leaveLobbyButton;

        public Text scoreText;
        public Text roundText;
        public Text playerANameText;
        public Text playerBNameText;
        public Image playerAHealth;
        public Image playerBHealth;
        public Text gameAddressText;

        public Text resultTitleText;
        public Text resultBodyText;
        public Button resultMenuButton;
        public Button resultStatisticsButton;

        public Text statisticsBodyText;
        public Button statisticsBackButton;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerScoreDisplay : MonoBehaviour
{

    public Color color;

    public Text text;

    public Image image;

    public Player player;

    string temporaryDisplayString;
    float temporaryDisplayTimeLeft;

    public bool useRoundWins;

    // Use this for initialization
    void Start()
    {
        image.color = color;
    }

    void Update()
    {
        if (temporaryDisplayTimeLeft > 0f)
        {
            temporaryDisplayTimeLeft -= Time.deltaTime;
            if (Time.time % 0.15f < 0.05f)
                text.color = player.color;
            else if (Time.time % 0.15f < 0.1f)
                text.color = Color.white;
            else text.color = Color.black;


        }
        else
        {
            text.color = player.color;
            if (useRoundWins)
                text.text = player.roundWins.ToString();
            else
                text.text = player.score.ToString();
        }
    }

    public void TemorarilyDisplay(string str, float time = 2f)
    {
        // A Rollback runs past Ticks again; their score popups already showed the first time.
        if (FrogSmashers.Netcode.SimulationDriver.InRollback)
            return;
        text.text = str;
        temporaryDisplayString = str;
        temporaryDisplayTimeLeft = time;
    }

}

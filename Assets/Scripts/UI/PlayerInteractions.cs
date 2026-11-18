using UnityEngine;

/// Shows the floating "BUILD BOTS" / "PROGRAM" buttons when the player is close enough.
public class PlayerInteractions : MonoBehaviour
{
    public WorldButton factoryButton;
    public WorldButton botButton;
    public FactoryPopup factoryPopup;
    public BotProgramPopup botPopup;

    public float botRadius = 3f;
    public Vector3 factoryButtonOffset = new Vector3(0f, 5.2f, 0f);
    public Vector3 botButtonOffset = new Vector3(0f, 3.8f, 0f);

    BotFactory shownFactory;
    ScoutBot shownBot;

    void Update()
    {
        if (UIState.ModalOpen) { HideAll(); return; }

        var f = BotFactory.Instance;
        if (f != null && !f.InRange(transform.position)) f = null;
        if (f != shownFactory)
        {
            if (shownFactory != null) shownFactory.SetHighlight(false);
            shownFactory = f;
            if (f != null)
            {
                f.SetHighlight(true);
                factoryButton.Show(f.transform, factoryButtonOffset, () => factoryPopup.Open(f));
            }
            else factoryButton.Hide();
        }

        ScoutBot best = null;
        float bestD = botRadius * botRadius;
        Vector3 me = transform.position;
        foreach (var b in ScoutBot.All)
        {
            Vector3 d = b.transform.position - me; d.y = 0f;
            float m = d.sqrMagnitude;
            if (m < bestD) { bestD = m; best = b; }
        }
        if (best != shownBot)
        {
            shownBot = best;
            if (best != null) botButton.Show(best.transform, botButtonOffset, () => botPopup.Open(best));
            else botButton.Hide();
        }
    }

    void HideAll()
    {
        if (shownFactory != null) shownFactory.SetHighlight(false);
        shownFactory = null;
        shownBot = null;
        factoryButton.Hide();
        botButton.Hide();
    }
}

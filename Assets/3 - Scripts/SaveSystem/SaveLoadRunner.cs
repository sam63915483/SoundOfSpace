using System.Collections;
using UnityEngine;

// Runs SaveSystem.Apply on a brief delay so all Awake/Start/first-FixedUpdate
// have completed first. This avoids Start() methods (e.g. PlayerController.Start
// resetting position to spawnPoint) and FixedUpdate physics defaults from
// clobbering the just-applied save state.
public class SaveLoadRunner : MonoBehaviour
{
    public void Run(SaveData data)
    {
        StartCoroutine(RunCoro(data));
    }

    IEnumerator RunCoro(SaveData data)
    {
        yield return null;                       // let all Start() run
        yield return new WaitForFixedUpdate();   // let initial FixedUpdate physics settle

        try { SaveSystem.Apply(data); }
        catch (System.Exception e) { Debug.LogError($"[SaveLoadRunner] Apply failed: {e}"); }

        Destroy(gameObject);
    }
}

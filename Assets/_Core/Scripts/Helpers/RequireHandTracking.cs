using System.Collections;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

public class RequireHandTracking : MonoBehaviour
{
    [SerializeField]
    private GameObject handTrackingMessage;

    [SerializeField]
    private GameObject menu;
    
    [SerializeField]
    private Hand leftHand;

    [SerializeField]
    private Hand rightHand;

    private bool _checkForHandTracking;
    private IEnumerator Start()
    {
        yield return null;
        
        _checkForHandTracking = true;
        handTrackingMessage.SetActive(true);
        menu.SetActive(false);
    }

    private void Update()
    {
        if (!_checkForHandTracking) return;
        var active = rightHand.IsConnected || leftHand.IsConnected;
        if (_checkForHandTracking != active) return;
        _checkForHandTracking = !active;
        handTrackingMessage.SetActive(!active);
        menu.SetActive(active);
        Debug.Log("set menu active");
    }
}

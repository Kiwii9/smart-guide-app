using UnityEngine;
using TMPro;
using System.Collections;

public class ARGuidanceController : MonoBehaviour
{
    [Header("Setup")]
    public GameObject arrowPrefab;
    public Transform userCamera;
    public Transform xrOrigin;

    [Header("Scanner Connection")]
    public QRScannerController scannerController;

    [Header("Adjustments")]
    public float arrowDistance = 2.0f;
    public float heightOffset = 0.3f;
    public float scanDistance = 0.5f;

    [Header("Destinations")]
    public Transform[] targets;

    private GameObject currentArrow;
    private TextMeshPro textComponent;
    private Transform currentTarget;
    private bool showMenu = false;
    private bool isRecalibrating = false;

    private Vector2 scrollPosition = Vector2.zero;

    void Start()
    {
        if (userCamera == null) userCamera = Camera.main.transform;
    }

    void Update()
    {
        if (currentTarget != null && currentArrow != null && currentArrow.activeSelf)
        {
            UpdateArrowPosition();
            UpdateArrowRotation();
            UpdateDistanceText();
        }
    }

    void OnGUI()
    {
        GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
        btnStyle.fontSize = 22; // Reduced font size slightly

        // Main Menu Toggle Button
        if (GUI.Button(new Rect(30, 30, 120, 60), showMenu ? "Close" : "Menu", btnStyle))
        {
            showMenu = !showMenu;
        }

        if (showMenu)
        {
            // Reduced width to 350 and adjusted height
            Rect scrollRect = new Rect(30, 100, 350, Screen.height - 150);

            // Calculate height: Scan button (70) + Targets (targets.Length * 60)
            float totalContentHeight = 80 + (targets.Length * 60);
            Rect viewRect = new Rect(0, 0, 320, totalContentHeight);

            scrollPosition = GUI.BeginScrollView(scrollRect, scrollPosition, viewRect);

            float currentY = 0;

            // --- SCAN QR CODE NOW AT THE TOP ---
            if (GUI.Button(new Rect(0, currentY, 300, 55), "Scan QR Code", btnStyle))
            {
                if (scannerController != null)
                {
                    scannerController.gameObject.SetActive(true);
                    scannerController.ResumeScanManual();
                }
                showMenu = false;
            }

            currentY += 70; // Space after the scan button

            // --- TARGET LIST ---
            for (int i = 0; i < targets.Length; i++)
            {
                if (GUI.Button(new Rect(0, currentY + (i * 60), 300, 50), targets[i].name, btnStyle))
                {
                    SetTarget(i);
                    showMenu = false;
                }
            }

            GUI.EndScrollView();
        }
    }

    public void SetTarget(int index)
    {
        currentTarget = targets[index];
        if (currentArrow == null) SpawnArrow();
        currentArrow.SetActive(true);
    }

    public void RecalibrateToRealCoordinates(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (!isRecalibrating) StartCoroutine(SmoothRecalibrateRoutine(worldPosition, worldRotation));
    }

    IEnumerator SmoothRecalibrateRoutine(Vector3 anchorPos, Quaternion anchorRot)
    {
        isRecalibrating = true;

        Vector3 targetCameraPos = anchorPos + (Vector3.forward * scanDistance);
        Vector3 posDifference = targetCameraPos - userCamera.position;
        Vector3 targetRigPos = xrOrigin.position + posDifference;
        targetRigPos.y = xrOrigin.position.y;

        float duration = 1.0f;
        float time = 0;
        Vector3 startRigPos = xrOrigin.position;

        while (time < duration)
        {
            xrOrigin.position = Vector3.Lerp(startRigPos, targetRigPos, time / duration);
            time += Time.deltaTime;
            yield return null;
        }

        xrOrigin.position = targetRigPos;
        isRecalibrating = false;

        if (scannerController != null) scannerController.gameObject.SetActive(false);
    }

    void SpawnArrow()
    {
        if (arrowPrefab != null)
        {
            currentArrow = Instantiate(arrowPrefab);
            textComponent = currentArrow.GetComponentInChildren<TextMeshPro>();
        }
    }

    void UpdateArrowPosition()
    {
        Vector3 d = userCamera.position + (userCamera.forward * arrowDistance);
        d.y = userCamera.position.y - heightOffset;
        currentArrow.transform.position = Vector3.Lerp(currentArrow.transform.position, d, Time.deltaTime * 5f);
    }

    void UpdateArrowRotation()
    {
        Vector3 d = currentTarget.position - currentArrow.transform.position;
        d.y = 0;
        if (d != Vector3.zero)
            currentArrow.transform.rotation = Quaternion.Slerp(currentArrow.transform.rotation, Quaternion.LookRotation(d) * Quaternion.Euler(0, 180, 0), Time.deltaTime * 5f);
    }

    void UpdateDistanceText()
    {
        if (textComponent != null)
            textComponent.text = $"{currentTarget.name}\n{Vector3.Distance(userCamera.position, currentTarget.position):F1}m";
    }
}
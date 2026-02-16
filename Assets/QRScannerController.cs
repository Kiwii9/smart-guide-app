using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ZXing;
using Firebase;
using Firebase.Firestore;
using Firebase.Extensions;
using Firebase.Auth;
using System;
using System.Collections;
using UnityEngine.Android;
using System.Collections.Generic;

public class QRScannerController : MonoBehaviour
{
    [Header("Integration")]
    public ARGuidanceController arController;
    public Transform arCamera;

    [Header("UI References")]
    [SerializeField] private GameObject _scannerUIPanel; // Assign the blue panel here
    [SerializeField] private RawImage _rawImageBackground;
    [SerializeField] private TextMeshProUGUI _textOut;
    [SerializeField] private Button _btnResumeScan;
    [SerializeField] private Button _btnCloseScanner; // Your "X" Button

    private bool _isCamAvaible = false;
    private bool _canScan = true;
    private WebCamTexture _cameraTexture;
    private FirebaseFirestore db;

    void Start()
    {
        InitFirebase();

        // Link the Resume button
        if (_btnResumeScan)
        {
            _btnResumeScan.onClick.AddListener(ResumeScanManual);
            _btnResumeScan.gameObject.SetActive(false);
        }

        // --- LINK THE X BUTTON ---
        if (_btnCloseScanner)
        {
            _btnCloseScanner.onClick.AddListener(CloseScanner);
        }

        StartCoroutine(CameraPermissionLoop());
    }

    private IEnumerator CameraPermissionLoop()
    {
#if UNITY_ANDROID
        while (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            yield return new WaitForSeconds(1f);
        }
#endif
        yield return StartCoroutine(SetUpCamera());
    }

    private IEnumerator SetUpCamera()
    {
        if (_cameraTexture != null) { _cameraTexture.Stop(); Destroy(_cameraTexture); }
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0) yield break;

        _cameraTexture = new WebCamTexture(devices[0].name);
        _cameraTexture.Play();
        while (_cameraTexture.width <= 16) yield return null;

        if (_rawImageBackground) _rawImageBackground.texture = _cameraTexture;
        _isCamAvaible = true;
    }

    void Update()
    {
        // Simulation keys
        if (Input.GetKeyDown(KeyCode.Alpha1)) FetchData("qr_entrance_01");
        if (Input.GetKeyDown(KeyCode.Alpha2)) FetchData("qr_entrance_02");
        if (Input.GetKeyDown(KeyCode.Alpha3)) FetchData("qr_entrance_03");
        if (Input.GetKeyDown(KeyCode.Alpha4)) FetchData("qr_entrance_04");

        if (!_isCamAvaible || _cameraTexture == null || !_cameraTexture.isPlaying) return;

        // Only scan if the panel is currently visible
        if (_canScan && _scannerUIPanel != null && _scannerUIPanel.activeSelf) Scan();
    }

    private void Scan()
    {
        try
        {
            IBarcodeReader reader = new BarcodeReader();
            Result result = reader.Decode(_cameraTexture.GetPixels32(), _cameraTexture.width, _cameraTexture.height);
            if (result == null) return;

            _canScan = false;
            Handheld.Vibrate();
            string qrId = result.Text.Trim();
            FetchData(qrId);
        }
        catch (Exception e) { Debug.Log("Scan Error: " + e.Message); _canScan = true; }
    }

    private void InitFirebase()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                db = FirebaseFirestore.DefaultInstance;
                FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync();
            }
        });
    }

    private void FetchData(string qrId)
    {
        if (db == null) return;

        db.Collection("qr_anchors").Document(qrId).GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || !task.Result.Exists) return;

            GameObject physicalQr = GameObject.Find(qrId.Trim());

            if (physicalQr != null && arController != null)
            {
                if (arCamera == null) arCamera = Camera.main.transform;

                // --- SMART ALIGNMENT ---
                Vector3 targetPos = physicalQr.transform.position;
                targetPos.y = 0;
                float targetWallAngle = physicalQr.transform.eulerAngles.y;
                float standBackDistance = -0.5f;
                Vector3 pushBackDir = Quaternion.Euler(0, targetWallAngle, 0) * Vector3.forward;
                targetPos -= pushBackDir * standBackDistance;

                float currentCamY = arCamera.eulerAngles.y;
                float rotationNeeded = targetWallAngle - currentCamY + 180f;
                arController.transform.RotateAround(arCamera.position, Vector3.up, rotationNeeded);

                Vector3 currentCamPos = new Vector3(arCamera.position.x, 0, arCamera.position.z);
                Vector3 moveDelta = targetPos - currentCamPos;
                arController.transform.position += moveDelta;

                if (_textOut) _textOut.text = $"<color=green>Aligned to {qrId}</color>";
                if (_btnResumeScan) _btnResumeScan.gameObject.SetActive(true);
            }
        });
    }

    // --- CLOSE SCANNER FUNCTION ---
    public void CloseScanner()
    {
        if (_scannerUIPanel != null)
        {
            // This is the line that actually hides the UI (cite: image_a810b0.png)
            _scannerUIPanel.SetActive(false);

            // Stops the camera to save resources
            if (_cameraTexture != null && _cameraTexture.isPlaying)
            {
                _cameraTexture.Stop();
            }
        }
        else
        {
            Debug.LogError("The Scanner UI Panel slot is empty in the Inspector!");
        }
    }

    public void ResumeScanManual()
    {
        _canScan = true;
        if (_textOut) _textOut.text = "Ready to Scan";
        if (_btnResumeScan) _btnResumeScan.gameObject.SetActive(false);
    }
}
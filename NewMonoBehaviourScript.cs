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
    [Header("UI References")]
    [SerializeField] private RawImage _rawImageBackground;
    [SerializeField] private AspectRatioFitter _aspectRatioFitter;
    [SerializeField] private TextMeshProUGUI _textOut;
    [SerializeField] private RectTransform _scanZone;
    [SerializeField] private Button _btnResumeScan;

    private bool _isCamAvaible = false;
    private bool _canScan = true;

    private WebCamTexture _cameraTexture;
    private FirebaseFirestore db;

    private Coroutine _scanTimeout; // Coroutine ··‹ Timeout

    void Start()
    {
        InitFirebase();

        _btnResumeScan.onClick.AddListener(ResumeScanManual);
        _btnResumeScan.gameObject.SetActive(false);

        StartCoroutine(CameraPermissionLoop());
    }

    private IEnumerator CameraPermissionLoop()
    {
#if UNITY_ANDROID
        while (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            _textOut.text = "Camera permission is required";
            Permission.RequestUserPermission(Permission.Camera);
            yield return new WaitForSeconds(1f);
        }
#endif
        yield return StartCoroutine(SetUpCamera());
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) return;

        if (!_isCamAvaible)
        {
            StartCoroutine(CameraPermissionLoop());
        }
    }

    private IEnumerator SetUpCamera()
    {
        if (_cameraTexture != null)
        {
            _cameraTexture.Stop();
            Destroy(_cameraTexture);
        }

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            _textOut.text = "No camera found";
            yield break;
        }

        foreach (var device in devices)
        {
            if (!device.isFrontFacing)
            {
                _cameraTexture = new WebCamTexture(device.name);
                break;
            }
        }

        _cameraTexture.Play();

        while (_cameraTexture.width <= 16)
            yield return null;

        _rawImageBackground.texture = _cameraTexture;
        _isCamAvaible = true;
        _textOut.text = "Camera Ready";
    }

    void Update()
    {
        if (!_isCamAvaible || _cameraTexture == null || !_cameraTexture.isPlaying)
            return;

        float ratio = (float)_cameraTexture.width / _cameraTexture.height;
        _aspectRatioFitter.aspectRatio = ratio;

        int rotation = _cameraTexture.videoRotationAngle;
        _rawImageBackground.rectTransform.localEulerAngles =
            new Vector3(0, 0, -rotation);

        float scaleY = _cameraTexture.videoVerticallyMirrored ? -1f : 1f;
        _rawImageBackground.rectTransform.localScale =
            new Vector3(1f, scaleY, 1f);

        if (_canScan)
            Scan();
    }

    private void Scan()
    {
        try
        {
            IBarcodeReader reader = new BarcodeReader();
            Result result = reader.Decode(
                _cameraTexture.GetPixels32(),
                _cameraTexture.width,
                _cameraTexture.height
            );

            if (result == null) return;

            _canScan = false;
            Handheld.Vibrate();

            string qrId = result.Text.Trim();

            // ﬁ»Ê· QR Ì»œ√ ›ﬁÿ »‹ "qr_entrance_"
            if (string.IsNullOrEmpty(qrId) || !qrId.StartsWith("qr_entrance_"))
            {
                _textOut.text = "Invalid QR Code";
                _canScan = false;
                ShowResumeButton();
                return;
            }

            // «·‰’ ’ÕÌÕ  «·»ÕÀ ›Ì Firebase
            _textOut.text = "Searching...";

            //  ‘€Ì· Timeout 5 ÀÊ«‰Ì
            if (_scanTimeout != null) StopCoroutine(_scanTimeout);
            _scanTimeout = StartCoroutine(ScanTimeout());

            FetchData(qrId);
        }
        catch (Exception e)
        {
            Debug.LogWarning("QR Scan Exception: " + e.Message);
            _textOut.text = "Scan Error";
            ShowResumeButton();
        }
    }

    private IEnumerator ScanTimeout()
    {
        yield return new WaitForSeconds(5f); // „Â·… 5 ÀÊ«‰Ì
        if (!_canScan) // ·« “«· „⁄·ﬁ
        {
            _textOut.text = "Service unavailable, try again";
            ShowResumeButton();
        }
    }

    private void InitFirebase()
    {
        FirebaseApp.CheckAndFixDependenciesAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.Result == DependencyStatus.Available)
                {
                    db = FirebaseFirestore.DefaultInstance;
                    FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync();
                }
                else
                {
                    _textOut.text = "Firebase Init Failed";
                }
            });
    }

    private void FetchData(string qrId)
    {
        if (db == null)
        {
            _textOut.text = "Service not ready, try again";
            ShowResumeButton();
            return;
        }

        db.Collection("qr_anchors").Document(qrId)
          .GetSnapshotAsync()
          .ContinueWithOnMainThread(task =>
          {
              // ≈Ìﬁ«› Timeout ⁄‰œ «·—œ
              if (_scanTimeout != null)
              {
                  StopCoroutine(_scanTimeout);
                  _scanTimeout = null;
              }

              ShowResumeButton(); // ÌŸÂ— «·“— œ«∆„«

              if (task.IsFaulted)
              {
                  _textOut.text = "Network Error!";
                  return;
              }

              DocumentSnapshot snap = task.Result;

              if (snap.Exists)
              {
                  var data = snap.ToDictionary();
                  string building = data.ContainsKey("buildingId") ? data["buildingId"].ToString() : "Unknown";
                  int floor = data.ContainsKey("floor") ? Convert.ToInt32(data["floor"]) : 0;
                  float x = 0f;
                  float y = 0f;

                  if (data.ContainsKey("anchorPos"))
                  {
                      var anchorPos = data["anchorPos"] as IDictionary<string, object>;
                      if (anchorPos != null)
                      {
                          if (anchorPos.ContainsKey("x")) x = Convert.ToSingle(anchorPos["x"]);
                          if (anchorPos.ContainsKey("y")) y = Convert.ToSingle(anchorPos["y"]);
                      }
                  }

                  _textOut.text =
                      $"Building: {building}\nFloor: {floor}\nanchorPos.x = {x}, anchorPos.y = {y})";
              }
              else
              {
                  _textOut.text = "QR code is not registered";
              }
          });
    }

    private void ShowResumeButton()
    {
        _btnResumeScan.gameObject.SetActive(true);
    }

    public void ResumeScanManual()
    {
        _canScan = true;
        _textOut.text = "Ready to scan...";
        _btnResumeScan.gameObject.SetActive(false);
    }
}

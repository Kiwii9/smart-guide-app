using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ZXing;                 // مكتبة قراءة QR Code
using Firebase;
using Firebase.Firestore;    // Firestore Database
using Firebase.Extensions;   // تشغيل Firebase على Main Thread
using Firebase.Auth;         // المصادقة (Authentication)
using System;
using System.Collections;
using UnityEngine.Android;   // صلاحيات أندرويد (الكاميرا)
using System.Collections.Generic;

public class QRScannerController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RawImage _rawImageBackground; 
    // لعرض صورة الكاميرا داخل واجهة Unity

    [SerializeField] private AspectRatioFitter _aspectRatioFitter; 
    // لضبط نسبة العرض إلى الارتفاع حسب الكاميرا

    [SerializeField] private TextMeshProUGUI _textOut; 
    // لعرض الرسائل (جاهز – خطأ – نتيجة QR)

    [SerializeField] private RectTransform _scanZone; 
    // منطقة المسح (غير مستخدمة هنا لكنها جاهزة للتطوير)

    [SerializeField] private Button _btnResumeScan; 
    // زر إعادة تفعيل المسح اليدوي

    private bool _isCamAvaible = false; 
    // هل الكاميرا جاهزة للعمل؟

    private bool _canScan = true; 
    // هل مسموح بالمسح الآن؟

    private WebCamTexture _cameraTexture; 
    // مصدر بث الكاميرا

    private FirebaseFirestore db; 
    // مرجع قاعدة بيانات Firestore

    private Coroutine _scanTimeout; 
    // Coroutine لإيقاف المسح إذا طال الانتظار (Timeout)

    void Start()
    {
        // تهيئة Firebase
        InitFirebase();

        // ربط زر الاستئناف بالدالة
        _btnResumeScan.onClick.AddListener(ResumeScanManual);

        // إخفاء الزر بالبداية
        _btnResumeScan.gameObject.SetActive(false);

        // بدء فحص صلاحية الكاميرا
        StartCoroutine(CameraPermissionLoop());
    }

    private IEnumerator CameraPermissionLoop()
    {
#if UNITY_ANDROID
        // التحقق من صلاحية الكاميرا في أندرويد
        while (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            _textOut.text = "Camera permission is required";
            Permission.RequestUserPermission(Permission.Camera);
            yield return new WaitForSeconds(1f);
        }
#endif
        // بعد الموافقة، تشغيل الكاميرا
        yield return StartCoroutine(SetUpCamera());
    }

    void OnApplicationFocus(bool hasFocus)
    {
        // عند الرجوع للتطبيق من الخلفية
        if (!hasFocus) return;

        // إذا الكاميرا غير جاهزة أعد طلب الصلاحية
        if (!_isCamAvaible)
        {
            StartCoroutine(CameraPermissionLoop());
        }
    }

    private IEnumerator SetUpCamera()
    {
        // إيقاف الكاميرا القديمة إن وجدت
        if (_cameraTexture != null)
        {
            _cameraTexture.Stop();
            Destroy(_cameraTexture);
        }

        // جلب جميع كاميرات الجهاز
        WebCamDevice[] devices = WebCamTexture.devices;

        // في حال عدم وجود كاميرا
        if (devices.Length == 0)
        {
            _textOut.text = "No camera found";
            yield break;
        }

        // اختيار الكاميرا الخلفية
        foreach (var device in devices)
        {
            if (!device.isFrontFacing)
            {
                _cameraTexture = new WebCamTexture(device.name);
                break;
            }
        }

        // تشغيل الكاميرا
        _cameraTexture.Play();

        // الانتظار حتى تبدأ الكاميرا فعليًا
        while (_cameraTexture.width <= 16)
            yield return null;

        // عرض بث الكاميرا في الواجهة
        _rawImageBackground.texture = _cameraTexture;
        _isCamAvaible = true;
        _textOut.text = "Camera Ready";
    }

    void Update()
    {
        // التأكد أن الكاميرا تعمل
        if (!_isCamAvaible || _cameraTexture == null || !_cameraTexture.isPlaying)
            return;

        // ضبط نسبة العرض
        float ratio = (float)_cameraTexture.width / _cameraTexture.height;
        _aspectRatioFitter.aspectRatio = ratio;

        // تدوير الصورة حسب وضعية الجهاز
        int rotation = _cameraTexture.videoRotationAngle;
        _rawImageBackground.rectTransform.localEulerAngles =
            new Vector3(0, 0, -rotation);

        // تصحيح الانعكاس العمودي
        float scaleY = _cameraTexture.videoVerticallyMirrored ? -1f : 1f;
        _rawImageBackground.rectTransform.localScale =
            new Vector3(1f, scaleY, 1f);

        // بدء المسح إذا كان مسموح
        if (_canScan)
            Scan();
    }

    private void Scan()
    {
        try
        {
            // إنشاء قارئ QR
            IBarcodeReader reader = new BarcodeReader();

            // محاولة قراءة QR من صورة الكاميرا
            Result result = reader.Decode(
                _cameraTexture.GetPixels32(),
                _cameraTexture.width,
                _cameraTexture.height
            );

            // إذا لم يتم العثور على QR
            if (result == null) return;

            // إيقاف المسح مؤقتًا
            _canScan = false;

            // اهتزاز الجهاز
            Handheld.Vibrate();

            string qrId = result.Text.Trim();

            // التحقق من أن QR يبدأ بالبادئة الصحيحة
            if (string.IsNullOrEmpty(qrId) || !qrId.StartsWith("qr_entrance_"))
            {
                _textOut.text = "Invalid QR Code";
                _canScan = false;
                ShowResumeButton();
                return;
            }

            // عرض حالة البحث
            _textOut.text = "Searching...";

            // تشغيل Timeout لمدة 5 ثواني
            if (_scanTimeout != null) StopCoroutine(_scanTimeout);
            _scanTimeout = StartCoroutine(ScanTimeout());

            // جلب البيانات من Firebase
            FetchData(qrId);
        }
        catch (Exception e)
        {
            // في حال حدوث خطأ غير متوقع
            Debug.LogWarning("QR Scan Exception: " + e.Message);
            _textOut.text = "Scan Error";
            ShowResumeButton();
        }
    }

    private IEnumerator ScanTimeout()
    {
        // انتظار 5 ثواني
        yield return new WaitForSeconds(5f);

        // إذا ما زال المسح متوقف
        if (!_canScan)
        {
            _textOut.text = "Service unavailable, try again";
            ShowResumeButton();
        }
    }

    private void InitFirebase()
    {
        // التحقق من جاهزية Firebase
        FirebaseApp.CheckAndFixDependenciesAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.Result == DependencyStatus.Available)
                {
                    // تهيئة Firestore
                    db = FirebaseFirestore.DefaultInstance;

                    // تسجيل دخول مجهول
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
        // إذا لم يكن Firebase جاهز
        if (db == null)
        {
            _textOut.text = "Service not ready, try again";
            ShowResumeButton();
            return;
        }

        // جلب مستند QR من Firestore
        db.Collection("qr_anchors").Document(qrId)
          .GetSnapshotAsync()
          .ContinueWithOnMainThread(task =>
          {
              // إيقاف Timeout عند وصول الرد
              if (_scanTimeout != null)
              {
                  StopCoroutine(_scanTimeout);
                  _scanTimeout = null;
              }

              // إظهار زر إعادة المسح
              ShowResumeButton();

              // خطأ في الشبكة
              if (task.IsFaulted)
              {
                  _textOut.text = "Network Error!";
                  return;
              }

              DocumentSnapshot snap = task.Result;

              if (snap.Exists)
              {
                  // تحويل البيانات إلى Dictionary
                  var data = snap.ToDictionary();

                  // قراءة البيانات الأساسية
                  string building = data.ContainsKey("buildingId") ? data["buildingId"].ToString() : "Unknown";
                  int floor = data.ContainsKey("floor") ? Convert.ToInt32(data["floor"]) : 0;

                  float x = 0f;
                  float y = 0f;

                  // قراءة إحداثيات الـ Anchor
                  if (data.ContainsKey("anchorPos"))
                  {
                      var anchorPos = data["anchorPos"] as IDictionary<string, object>;
                      if (anchorPos != null)
                      {
                          if (anchorPos.ContainsKey("x")) x = Convert.ToSingle(anchorPos["x"]);
                          if (anchorPos.ContainsKey("y")) y = Convert.ToSingle(anchorPos["y"]);
                      }
                  }

                  // عرض النتيجة
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
        // إظهار زر إعادة المسح
        _btnResumeScan.gameObject.SetActive(true);
    }

    public void ResumeScanManual()
    {
        // إعادة تفعيل المسح
        _canScan = true;
        _textOut.text = "Ready to scan...";
        _btnResumeScan.gameObject.SetActive(false);
    }
}

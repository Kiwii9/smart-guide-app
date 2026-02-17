# AR Indoor Navigation System (Unity + Firebase)

This project is an Augmented Reality (AR) indoor navigation application designed for college campuses. It uses **QR code markers** for precise localization and recalibration, allowing users to navigate complex hallways with a 3D guidance arrow.

The system is built on **Unity**, uses **Firebase Firestore** for dynamic data storage, and implements **ZXing** for barcode scanning.

---

## 📂 Key Scripts & Architecture

The core logic is divided into two primary controllers: the **Scanner** (localization) and the **Guidance** (navigation).

### 1. QRScannerController.cs
**Role:** The "Eyes" of the system. Handles camera permissions, QR code decoding, cloud data fetching, and the mathematical alignment of the AR world.

#### Key Features:
* **Smart World Alignment:** Instead of hard-coding directions (North/South), the script dynamically reads the rotation of 3D marker objects in the Unity scene.
    * *Math:* It calculates the delta between the camera's current rotation and the physical object's rotation (`targetWallAngle - currentCamY`) and rotates the entire `ARSessionOrigin` around the user's head.
* **Position Offset logic:** Prevents the "Wall Clipping" issue.
    * When a user scans a code, the system assumes they are standing **0.5 meters away** from the wall.
    * *Code:* `targetPos -= pushBackDir * 0.5f;`
* **Firebase Integration:** Connects to the `qr_anchors` collection in Firestore to validate scanned IDs before aligning.
* **Battery Optimization:** Automatically stops the `WebCamTexture` when the scanner UI is closed to save mobile battery life.

#### Inspector Variables:
* `arController`: Reference to the XR Origin/AR Session.
* `Scanner UI Panel`: The Canvas GameObject that toggles on/off.
* `Close Button`: Triggers the cleanup and hiding of the camera feed.

---

### 2. ARGuidanceController.cs
**Role:** The "Brain" of the system. Manages the destination menu, spawns the navigation arrow, and handles the user interface (UI).

#### Key Features:
* **Dynamic Scrollable Menu (OnGUI):**
    * Implements a custom `GUI.BeginScrollView` loop.
    * **Auto-Scaling:** The menu height dynamically expands based on the number of classrooms in the `Targets` array (`targets.Length * 60`).
    * **UX:** Places the "Scan QR Code" button at the very top for quick access during recalibration.
* **3D Arrow Logic:**
    * Spawns an arrow prefab that floats in front of the user (`arrowDistance = 2.0f`).
    * **Rotation:** Uses `Quaternion.LookRotation` to ensure the arrow always points toward the selected destination, updating every frame.
    * **Distance Calculation:** Displays real-time distance in meters using `Vector3.Distance`.
* **Smooth Recalibration:** Includes a coroutine to smoothly interpolate the camera position when aligning, preventing jarring "snaps" for the user.

#### Inspector Variables:
* `Targets`: An array of Transforms representing classrooms (POIs).
* `Arrow Prefab`: The 3D model used for the pointer.
* `Scan Distance`: Distance offset for recalibration.

---

## 🛠️ Setup & Configuration

### 1. Hierarchy Requirements
To ensure the scripts find the correct objects, the Unity Hierarchy must follow this naming convention:
* **Physical QR Markers:** Named `qr_entrance_01`, `qr_entrance_02`, etc. These must be placed in the scene where they exist in the real world.
* **POIS Parent:** A container object holding all classroom destinations.

### 2. Firebase Structure
The Firestore database must use the following schema:
* **Collection:** `qr_anchors`
* **Document ID:** Matches the physical QR content (e.g., `qr_entrance_03`).
* **Fields:**
    * `direction` (string): "north", "south", "east", "west" (used for verification).
    * `anchorPos` (map): `{ x: 0, y: 0 }`.

### 3. Android Permissions
The `QRScannerController` handles the Android Camera Permission loop automatically.
* If permission is denied, it waits 1 second and requests again.

---

## 🚀 How It Works (User Flow)

1.  **Start:** User opens the app. The map is loaded, but the device doesn't know where it is relative to the building.
2.  **Scan:** User opens the Menu, taps **"Scan QR Code"**, and points the camera at a wall marker.
3.  **Align:**
    * App reads the QR ID.
    * App finds the matching blue cube in the Unity Scene.
    * App rotates and transports the **entire virtual world** so that the virtual wall matches the real wall.
4.  **Navigate:** User selects "Class 1165" from the scrollable list. A 3D arrow appears, guiding them to the destination.

---

## 📝 Dependencies
* **AR Foundation** (Unity Package)
* **Firebase Firestore** (SDK)
* **ZXing** (Barcode Scanner Library)
* **TextMeshPro** (For UI text rendering)
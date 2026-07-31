# Product Requirements Document (PRD): Mobile Web Camera Card Scanner

## 1. Overview

The goal of this feature is to allow users to quickly scan trading cards via a mobile web browser. The feature will capture an image using the device's native camera, crop the background to isolate the card, pass it to our existing card recognition engine, and return the matched card details from the database.

This must be a lightweight addition that heavily leverages the existing scanning and database infrastructure already present in the application.

## 2. User Flow

1. **Trigger:** The user taps a new "Scan Card" button on the mobile web interface.
2. **Capture:** A camera view opens within the web app (requesting permissions if necessary). The user snaps a photo of a single card and confirms the capture.
3. **Processing (Backend/Edge):** The service receives the image and runs a trimming/cropping algorithm to isolate the card boundaries and remove the background.
4. **Recognition:** The cropped image is passed to the application's _existing_ scan logic and card database to identify the match.
5. **Success State:** The application displays the original, high-quality art image of the matched card along with its details from the database.
6. **Failure/Correction State:** If the card is not recognized, or if the user identifies it as a mismatch, the user is presented with a standard text-search input (re-using the existing manual search UI) to find and correct the entry.

## 3. Functional Requirements

### 3.1 Frontend (Mobile Web)

- **Camera Integration:** Implement standard HTML5 `getUserMedia` or `<input type="file" accept="image/*" capture="environment">` to access the rear-facing camera.
- **UI Elements:**
  - "Scan Card" entry button.
  - Camera viewfinder or native camera trigger.
  - Loading indicator during image upload, processing, and recognition.
  - Success modal/screen displaying the retrieved database image and metadata.
- **Correction UI:** A fallback workflow seamlessly routing the user to the existing text-search component if the scan fails or needs manual override.

### 3.2 Image Processing (Auto-Crop)

- The system must detect the rectangular edges of the trading card within the raw photo.
- The background must be cropped out, leaving only the card surface.
- _Note to LLM:_ Propose a lightweight solution for this edge detection and cropping (e.g., a lightweight client-side canvas manipulation or a fast server-side OpenCV/ImageMagick script) that fits seamlessly into a standard web stack.

### 3.3 Backend Integration

- **API Endpoint:** A lightweight endpoint to receive the raw or pre-cropped image from the frontend.
- **Existing Logic Re-use:** The feature must strictly utilize the existing scan logic for pattern recognition/matching. Do not engineer a new recognition model.
- **Database Query:** Upon a successful match from the scan logic, retrieve the master art image and card metadata from the existing inventory database.

## 4. Non-Functional Requirements

- **Performance:** The image upload, crop, and match sequence must feel snappy on a mobile 4G/5G connection. Image payloads should be compressed before transmission if processed server-side.
- **Lightweight Implementation:** Avoid heavy ML models on the client side if they negatively impact load times. Prioritize simple, fast edge-detection for the cropping phase.
- **Responsive Design:** The camera UI and results screen must be optimized for mobile viewport dimensions.

## 5. Out of Scope

- Building a new OCR or image recognition engine (we are using the existing one).
- Bulk scanning (this feature is single-card capture).
- Changes to the core database schema.

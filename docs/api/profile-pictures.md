# Private Profile pictures

Each signed-in person can upload, read, replace, or remove one Profile picture through the
authenticated Profile API. The Profile representation returned by registration, sign-in,
`GET /api/profile`, and `PUT /api/profile` includes `hasPicture`. It is `false` before the first
upload and after removal.

The API accepts one multipart field named `File` containing a static JPEG, PNG, or WebP image.
The encoded upload is limited to 2 MB and each decoded dimension is limited to 4096 pixels.
The API detects the format from the image bytes, then re-encodes the image in that format and at
the same dimensions to remove embedded metadata. The submitted filename and content type do not
choose the stored format.

```http
PUT /api/profile/picture
Authorization: Bearer <access-token>
Content-Type: multipart/form-data; boundary=...
```

On success, the API returns `204 No Content`. Invalid, unsupported, animated, oversized, or
over-dimension images return `400 Bad Request` with a detail naming the violated rule. The image
is private: requests without a valid bearer token receive `401`, and a Profile cannot name an
owner in the route or request body.

Read the current image with `GET /api/profile/picture`. The response body contains the image
bytes and its detected media type. An absent image returns `404`; an object-storage failure is a
server error and is never reported as absence. The response uses `Cache-Control: no-store` so a
browser cannot keep showing an outdated replacement. Remove the current image with
`DELETE /api/profile/picture`; it returns `204` even when no picture exists.

## Development loops

Start the configured stack or object store using the [README setup](../../README.md#two-dev-loops),
then obtain a bearer token for a confirmed Profile. The Docker API base URL is
`http://pitaka.localhost`; the SDK API base URL is `http://localhost:5044`. The API talks to the
private object store using its configured `ObjectStorage__...` values in either loop.

Use the same API requests in both loops, changing only `API_BASE`:

```bash
export API_BASE=http://pitaka.localhost # use http://localhost:5044 in the SDK loop
export ACCESS_TOKEN=<confirmed-profile-bearer-token>

curl --fail-with-body -X PUT "$API_BASE/api/profile/picture" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -F "File=@./profile-picture.jpg"

curl --fail-with-body "$API_BASE/api/profile/picture" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -o ./profile-picture-read-back.jpg
```

## Browser clients

Because the image route requires a bearer token, fetch the bytes with an Authorization header
and display them through a local Blob URL. Revoke the previous URL when replacing the displayed
picture and when the view is disposed:

```js
const response = await fetch(`${apiBaseUrl}/api/profile/picture`, {
  headers: { Authorization: `Bearer ${accessToken}` },
});
if (!response.ok) throw new Error(`Picture fetch failed: ${response.status}`);

const nextUrl = URL.createObjectURL(await response.blob());
imageElement.src = nextUrl;
if (previousUrl) URL.revokeObjectURL(previousUrl);
previousUrl = nextUrl;
```

The browser never receives the bucket address, object key, or storage credentials.

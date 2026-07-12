"use client";

import { useEffect, useRef, useState } from "react";
import type { DragEvent } from "react";
import { Badge, Button, FileInput, HelperText, Label, Spinner, TextInput } from "flowbite-react";
import { ArrowIcon } from "@/components/icons/ArrowIcon";
import { DragHandleIcon } from "@/components/icons/DragHandleIcon";
import { TrashIcon } from "@/components/icons/TrashIcon";
import { CarImage } from "@/components/CarImage";
import { generateThumbnail, requestUploadUrl } from "@/lib/auction-actions";
import type { AuctionImageInput } from "@/types/auction-form";

/** Requirements.md §3.1 -- Images__MaxSizeMB default. */
const MAX_SIZE_BYTES = 5 * 1024 * 1024;
/** Requirements.md §3.1 -- Images__MaxPerAuction default. */
const MAX_IMAGES = 10;
const ALLOWED_CONTENT_TYPES = ["image/jpeg", "image/png", "image/webp"];

/**
 * One gallery entry as managed by this component. A superset of
 * `AuctionImageInput` (the wire shape) -- the extra fields track
 * client-only upload/thumbnail progress and are stripped by
 * `imagesToPayload` before the form submits.
 */
export interface ManagedImage {
  /** Client-only React key -- never sent to the backend. */
  clientId: string;
  /** Final object/external URL once ready; a local blob preview while uploading. */
  url: string;
  thumbnailUrl?: string | null;
  /** The GUID object key from `upload-url` -- present only for platform-hosted (uploaded) images, which is what makes "Generate thumbnail" (6.4) available for them. URL-fallback entries never have one. */
  key?: string;
  source: "upload" | "url";
  status: "uploading" | "ready" | "error";
  error?: string;
  thumbnailStatus?: "generating" | "error";
}

/**
 * Converts the manager's working state into the `AuctionImageInput[]` the
 * backend expects: only successfully uploaded/added images, in on-screen
 * order (which is also gallery order -- index 0 is the primary image).
 */
export function imagesToPayload(images: ManagedImage[]): AuctionImageInput[] {
  return images
    .filter((image) => image.status === "ready")
    .map((image, index) => ({
      url: image.url,
      thumbnailUrl: image.thumbnailUrl ?? undefined,
      sortOrder: index,
    }));
}

function isValidHttpUrl(value: string): boolean {
  try {
    const parsed = new URL(value);
    return parsed.protocol === "http:" || parsed.protocol === "https:";
  } catch {
    return false;
  }
}

interface AuctionImageManagerProps {
  /** Uncontrolled-style initial value -- only read once (mirrors react-hook-form's `Controller` `field.value` at mount). Every change after that flows out via `onChange`, not back in via this prop, so async upload updates never race a parent re-render. */
  initialImages?: ManagedImage[];
  onChange: (images: ManagedImage[]) => void;
  disabled?: boolean;
}

/**
 * Multi-image picker for the auction create/edit form (Task 6.3/6.4):
 * - Client-side pre-validation (type whitelist, ≤5 MB) before any network call
 * - Each accepted file uploads directly to storage via a presigned PUT
 *   (`requestUploadUrl` Server Action for the URL, then a plain browser
 *   `fetch` PUT straight to it -- see the Task 6 brief's architectural
 *   constraint on why the file itself never touches a Server Action)
 * - Drag-to-reorder (HTML5 DnD) sets the primary image (index 0); up/down
 *   buttons give the same capability without a pointer (Docs/DesignGuide.md §9)
 * - A plain URL input as a fallback for externally hosted images
 * - An optional per-image "Generate thumbnail" step (6.4) for uploaded
 *   (key-bearing) images
 */
export function AuctionImageManager({ initialImages = [], onChange, disabled }: AuctionImageManagerProps) {
  const [images, setImages] = useState<ManagedImage[]>(initialImages);
  const [fileErrors, setFileErrors] = useState<string[]>([]);
  const [urlInput, setUrlInput] = useState("");
  const [urlError, setUrlError] = useState<string | null>(null);
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Local blob preview URLs (used while a file is mid-upload) are only ever
  // useful in this tab -- revoke them on unmount so they don't leak memory.
  useEffect(() => {
    return () => {
      images.forEach((image) => {
        if (image.status === "uploading" && image.url.startsWith("blob:")) {
          URL.revokeObjectURL(image.url);
        }
      });
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // The parent's react-hook-form field is the single source of truth for
  // validation/submit; every local mutation (upload progress, reorder,
  // remove, thumbnail) flows out here rather than through the field's own
  // onChange directly, which would race against itself across concurrent
  // per-file uploads (see the doc comment above `uploadOne`).
  useEffect(() => {
    onChange(images);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [images]);

  async function uploadOne(file: File, clientId: string) {
    const result = await requestUploadUrl(file.type, file.size);

    if (!result.success) {
      setImages((prev) =>
        prev.map((image) =>
          image.clientId === clientId
            ? { ...image, status: "error", error: result.error.detail ?? result.error.title }
            : image
        )
      );
      return;
    }

    const { key, uploadUrl, objectUrl } = result.data;

    try {
      const putRes = await fetch(uploadUrl, {
        method: "PUT",
        headers: { "Content-Type": file.type },
        body: file,
      });

      if (!putRes.ok) {
        throw new Error(`Upload to storage failed with status ${putRes.status}`);
      }

      setImages((prev) => {
        const existing = prev.find((image) => image.clientId === clientId);
        if (existing?.url.startsWith("blob:")) URL.revokeObjectURL(existing.url);
        return prev.map((image) =>
          image.clientId === clientId ? { ...image, status: "ready", url: objectUrl, key } : image
        );
      });
    } catch {
      setImages((prev) =>
        prev.map((image) =>
          image.clientId === clientId
            ? { ...image, status: "error", error: "Upload to storage failed. Remove it and try again." }
            : image
        )
      );
    }
  }

  function handleFilesSelected(fileList: FileList | null) {
    if (!fileList || fileList.length === 0) return;

    const files = Array.from(fileList);
    const remainingSlots = MAX_IMAGES - images.length;
    const errors: string[] = [];
    const accepted: File[] = [];

    for (const file of files) {
      if (accepted.length >= remainingSlots) {
        errors.push(`${file.name}: skipped -- an auction can have at most ${MAX_IMAGES} images.`);
        continue;
      }
      if (!ALLOWED_CONTENT_TYPES.includes(file.type)) {
        errors.push(`${file.name}: unsupported file type -- use JPEG, PNG, or WebP.`);
        continue;
      }
      if (file.size > MAX_SIZE_BYTES) {
        errors.push(`${file.name}: exceeds the 5 MB limit.`);
        continue;
      }
      accepted.push(file);
    }

    setFileErrors(errors);
    if (fileInputRef.current) fileInputRef.current.value = "";
    if (accepted.length === 0) return;

    const placeholders: ManagedImage[] = accepted.map((file) => ({
      clientId: crypto.randomUUID(),
      url: URL.createObjectURL(file),
      source: "upload",
      status: "uploading",
    }));
    setImages((prev) => [...prev, ...placeholders]);

    accepted.forEach((file, i) => {
      void uploadOne(file, placeholders[i].clientId);
    });
  }

  function handleAddUrl() {
    const trimmed = urlInput.trim();
    if (!trimmed) return;

    if (images.length >= MAX_IMAGES) {
      setUrlError(`An auction can have at most ${MAX_IMAGES} images.`);
      return;
    }
    if (!isValidHttpUrl(trimmed)) {
      setUrlError("Enter a valid image URL (must start with http:// or https://).");
      return;
    }

    setUrlError(null);
    setImages((prev) => [
      ...prev,
      { clientId: crypto.randomUUID(), url: trimmed, source: "url", status: "ready" },
    ]);
    setUrlInput("");
  }

  function handleRemove(clientId: string) {
    setImages((prev) => {
      const target = prev.find((image) => image.clientId === clientId);
      if (target?.url.startsWith("blob:")) URL.revokeObjectURL(target.url);
      return prev.filter((image) => image.clientId !== clientId);
    });
  }

  function moveImage(clientId: string, direction: -1 | 1) {
    setImages((prev) => {
      const index = prev.findIndex((image) => image.clientId === clientId);
      const target = index + direction;
      if (index === -1 || target < 0 || target >= prev.length) return prev;
      const next = [...prev];
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  function reorderTo(targetIndex: number) {
    setImages((prev) => {
      if (dragIndex === null || dragIndex === targetIndex) return prev;
      const next = [...prev];
      const [moved] = next.splice(dragIndex, 1);
      next.splice(targetIndex, 0, moved);
      return next;
    });
    setDragIndex(null);
  }

  async function handleGenerateThumbnail(clientId: string) {
    const image = images.find((img) => img.clientId === clientId);
    if (!image?.key) return;

    setImages((prev) =>
      prev.map((img) => (img.clientId === clientId ? { ...img, thumbnailStatus: "generating" } : img))
    );

    const result = await generateThumbnail(image.key);

    setImages((prev) =>
      prev.map((img) => {
        if (img.clientId !== clientId) return img;
        if (!result.success) {
          return { ...img, thumbnailStatus: "error", error: result.error.detail ?? result.error.title };
        }
        return { ...img, thumbnailStatus: undefined, thumbnailUrl: result.data.thumbnailUrl, error: undefined };
      })
    );
  }

  const isDisabled = Boolean(disabled);

  return (
    <div className="space-y-4">
      <div>
        <Label htmlFor="auction-images">Photos</Label>
        <FileInput
          ref={fileInputRef}
          id="auction-images"
          accept={ALLOWED_CONTENT_TYPES.join(",")}
          multiple
          disabled={isDisabled || images.length >= MAX_IMAGES}
          onChange={(event) => handleFilesSelected(event.currentTarget.files)}
        />
        <HelperText>
          {images.length} of {MAX_IMAGES} images -- JPEG, PNG, or WebP, up to 5 MB each. Drag to reorder;
          the first image is the primary image shown in listings.
        </HelperText>
        {fileErrors.length > 0 && (
          <ul className="mt-1 space-y-0.5">
            {fileErrors.map((message) => (
              <li key={message} className="text-sm text-red-600">
                {message}
              </li>
            ))}
          </ul>
        )}
      </div>

      {images.length > 0 && (
        <ul className="space-y-2">
          {images.map((image, index) => (
            <li
              key={image.clientId}
              draggable={!isDisabled}
              onDragStart={() => setDragIndex(index)}
              onDragOver={(event: DragEvent<HTMLLIElement>) => event.preventDefault()}
              onDrop={() => reorderTo(index)}
              className="flex items-center gap-3 rounded-lg border border-slate-200 bg-white p-2"
            >
              <span className="cursor-grab text-slate-400" aria-hidden="true">
                <DragHandleIcon className="h-5 w-5" />
              </span>

              <div className="relative h-16 w-20 flex-shrink-0 overflow-hidden rounded-md">
                <CarImage
                  src={image.status === "uploading" ? image.url : (image.thumbnailUrl ?? image.url)}
                  alt={`Gallery image ${index + 1}`}
                  className={`h-full w-full${image.status === "uploading" ? " opacity-60" : ""}`}
                />
                {image.status === "uploading" && (
                  <div className="absolute inset-0 flex items-center justify-center bg-black/20">
                    <Spinner size="sm" color="gray" aria-label="Uploading" />
                  </div>
                )}
              </div>

              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  {index === 0 && (
                    <Badge color="primary" className="shrink-0">
                      Primary
                    </Badge>
                  )}
                  <span className="truncate text-sm text-slate-600">
                    {image.source === "upload" ? "Uploaded" : "External URL"}
                  </span>
                </div>
                {image.status === "uploading" && <p className="text-sm text-slate-500">Uploading…</p>}
                {image.status === "error" && <p className="text-sm text-red-600">{image.error}</p>}
                {image.thumbnailStatus === "error" && (
                  <p className="text-sm text-red-600">Thumbnail failed: {image.error}</p>
                )}

                {image.status === "ready" && image.key && (
                  <Button
                    type="button"
                    size="xs"
                    color="light"
                    disabled={isDisabled || image.thumbnailStatus === "generating"}
                    onClick={() => handleGenerateThumbnail(image.clientId)}
                    className="mt-1"
                  >
                    {image.thumbnailStatus === "generating" ? (
                      <>
                        <Spinner size="xs" className="mr-2" /> Generating…
                      </>
                    ) : image.thumbnailUrl ? (
                      "Regenerate thumbnail"
                    ) : (
                      "Generate thumbnail"
                    )}
                  </Button>
                )}
              </div>

              <div className="flex flex-shrink-0 items-center gap-1">
                <Button
                  type="button"
                  size="xs"
                  color="light"
                  disabled={isDisabled || index === 0}
                  aria-label="Move image up"
                  onClick={() => moveImage(image.clientId, -1)}
                >
                  <ArrowIcon direction="up" className="h-4 w-4" />
                </Button>
                <Button
                  type="button"
                  size="xs"
                  color="light"
                  disabled={isDisabled || index === images.length - 1}
                  aria-label="Move image down"
                  onClick={() => moveImage(image.clientId, 1)}
                >
                  <ArrowIcon direction="down" className="h-4 w-4" />
                </Button>
                <Button
                  type="button"
                  size="xs"
                  color="failure"
                  disabled={isDisabled}
                  aria-label="Remove image"
                  onClick={() => handleRemove(image.clientId)}
                >
                  <TrashIcon className="h-4 w-4" />
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <div>
        <Label htmlFor="auction-image-url">Or add an image by URL</Label>
        <div className="flex gap-2">
          <TextInput
            id="auction-image-url"
            className="flex-1"
            placeholder="https://example.com/car.jpg"
            value={urlInput}
            disabled={isDisabled || images.length >= MAX_IMAGES}
            onChange={(event) => {
              setUrlInput(event.currentTarget.value);
              setUrlError(null);
            }}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                event.preventDefault();
                handleAddUrl();
              }
            }}
          />
          <Button
            type="button"
            color="light"
            disabled={isDisabled || images.length >= MAX_IMAGES}
            onClick={handleAddUrl}
          >
            Add
          </Button>
        </div>
        {urlError && <p className="mt-1 text-sm text-red-600">{urlError}</p>}
      </div>
    </div>
  );
}

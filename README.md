# Playnite Extensions

The repository for all [Playnite](https://github.com/JosefNemec/Playnite) extensions I have created.

## Installation

All extension can be installed directly through the add-on browser in Playnite (`F9`).

## Extensions

### Metadata Image Optimizer

Automatically converts images to a user-defined format, while also resizing all images above a maximum size.  
Users can modify a number of quality-related settings based on the format used to achieve a balance between size and image quality.  

Encoding, resizing and conversion are done by [ImageOptimizer](https://github.com/Fileover/ImageOptimizer)
(libjxl, libwebp, MozJPEG and OxiPNG), which is bundled with the extension as `ImageOptimizer.exe`.
It is invoked as a separate process; the extension itself only extracts icons and updates the Playnite
database. ImageOptimizer is licensed under GPL-3.0-or-later, see `THIRD-PARTY-NOTICES.md` in its
repository.

For a full list of features and examples, see this [Wiki page](../../wiki/Metadata-Image-Optimizer).

# Third-Party Notices

Keystream itself is licensed under the MIT License. See `LICENSE` at the
repository root: Copyright (c) 2026 FLD.

Keystream is a non-commercial project. Donations, if any, are voluntary
only. No commercial licence for Interception has been acquired, and none
is needed while the project stays non-commercial. If the project ever
becomes commercial, a commercial Interception licence must be acquired
first. See section 1 below.

This file lists third-party components redistributed with Keystream,
what was checked for each one, and the full licence texts that must
travel with a distribution. Verification was done on 29 October 2026
against the tree at `~/workspace/keystream-update-terbaru/keystream`,
the official Interception release download, and the NuGet package
cache at `~/tmpdir/nugetpkgs`. Claims below state their evidence.
Anything not verified is marked as not verified. Do not treat this
file as legal advice.

## 1. Interception (library and driver installer)

- Component: Interception, version 1.0.1, by Francisco Lopes
  (oblitum).
- Source: https://github.com/oblitum/Interception
- Release page: https://github.com/oblitum/Interception/releases/tag/v1.0.1
- Release asset: https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip
- Release record: GitHub release id 6362029, tag v1.0.1, published
  2017-05-12T05:25:28Z. The asset in the release record is 389119
  bytes, matching the downloaded file.
- Downloaded asset SHA-256:
  ad038963d6413055765128b0b931f6e765147c9916dba79e65d872b261f9af10
  (389119 bytes). The same SHA-256 was measured for the copy kept
  in the installer snapshot at `~/workspace/keystream-installer/Interception.zip`.
- File used by Keystream: `Interception/library/x64/interception.dll`
  inside that asset.
  - Size: 11264 bytes.
  - MD5: fe8b2a022297aa36a3546391221f635a
  - SHA-256: ab88164c11b1b48488772d4c3bfaa4509d5b0ae9dbc5a691dc4f96f0260443c8
  - The file is byte-identical (`cmp`) to the single source copy in
    the tree, `Keystream.Windows/interception.dll`.
  - All 12 copies of `interception.dll` found in the tree (source
    copy, build outputs under `bin/`, and `publish-app/` /
    `publish-staging/`) have the same MD5, SHA-256, and size.
  - The x86 DLL in the same asset is a different file (10752 bytes)
    and is not used: Keystream targets x64 only.
- Driver installer in the same asset:
  `Interception/command line installer/install-interception.exe`,
  470528 bytes.
  - SHA-256: e137863a79da797f08e7a137280ff2a123809044a888fd75ce9c973198915abe
  - MD5: 0f0b50d92e030b8965ce669c8058fa6e
  - The hash was measured from the downloaded asset and matches the
    extracted copy in the installer snapshot. The installer binary
    is embedded by the Setup build only; it is not committed to the
    repository (see `.gitignore`).
- Status: UNMODIFIED. The DLL in the tree is byte-identical to the
  official release DLL. No Interception source code (`.c`, `.h`
  implementation, driver source) is present in the Keystream tree
  beyond the P/Invoke declarations. The DLL is used via P/Invoke
  only: `Keystream.Windows/Interception/NativeInterception.cs`
  declares the C API with `[DllImport("interception.dll",
  CallingConvention = CallingConvention.Cdecl)]`, and
  `Keystream.App/Keystream.App.csproj` copies
  `Keystream.Windows/interception.dll` next to the application as
  content. Communication with the driver happens solely through
  this library and its API. A declaration-by-declaration diff
  against the vendor header `interception.h` was not done in
  Batch 1 and is not claimed here.
- Licence: dual-licensed. The upstream README states:

  > Interception is dual-licensed.
  >
  > For non-commercial purposes it adopts LGPL for the library and
  > its source code, with rights of distribution of the related
  > binary assets (drivers and installers) once communication with
  > drivers happen solely by use of the library and its API.

  For commercial purposes the README points to two other licences
  in the `licenses/commercial-usage/` directory and gives a contact
  address for acquiring a commercial licence. Keystream relies on
  the non-commercial LGPL terms only, consistent with the
  non-commercial statement at the top of this file.

- Licence text location: the release asset contains
  `Interception/licenses/non-commercial-usage/LGPL 3.0.txt`
  (7651 bytes, GNU Lesser General Public License version 3,
  29 June 2007). The full text of that file is reproduced in
  Appendix A of this file. The release asset also contains the
  two commercial licence PDFs under `licenses/commercial-usage/`;
  they are not reproduced here because they do not apply to this
  non-commercial use.

- What the LGPL requires for a Combined Work: LGPL version 3,
  section 4, requires each of the following (quoted from the
  licence text in Appendix A):

  > a) Give prominent notice with each copy of the Combined Work
  > that the Library is used in it and that the Library and its
  > use are covered by this License.
  >
  > b) Accompany the Combined Work with a copy of the GNU GPL and
  > this license document.

  Section 4(d)(1) additionally requires a suitable shared library
  mechanism: one that uses at run time a copy of the Library
  already present on the user's computer system and that will
  operate properly with a modified, interface-compatible version
  of the Library. Keystream ships `interception.dll` as a separate
  file next to the application and loads it through P/Invoke, so
  a recipient can replace that file with a modified,
  interface-compatible build. Whether that fully satisfies
  section 4(d) was not independently verified in Batch 1.

- GPL text (LGPL section 4(b)): the full GNU GPL version 3 text
  is included in this repository as `GPL-3.0.txt` at the root
  (35,149 bytes, SHA-256
  3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986,
  fetched verbatim from https://www.gnu.org/licenses/gpl-3.0.txt
  and cross-checked against the same URL). The release bundle
  must include this file next to the application so that LGPL
  section 4(b) is met for the GPL document as well as for the
  LGPL document.

- Driver binaries: the drivers themselves are distributed only
  inside the official installer binary. Their separate hashes
  were not verified in Batch 1, because they are not separate
  files in the tree or in the release asset listing. Do not claim
  separate driver verification.

## 2. NAudio

- Component: NAudio, version 2.2.1, authors Mark Heath and
  Contributors. Referenced by `Keystream.Actions` in
  `Keystream.Actions/Keystream.Actions.csproj` as
  `<PackageReference Include="NAudio" Version="2.2.1" />`.
- Project: https://github.com/naudio/NAudio (from the package
  metadata).
- What was checked: the package metadata file
  `naudio.nuspec` and the licence file `license.txt` inside the
  extracted NuGet package at
  `~/tmpdir/nugetpkgs/naudio/2.2.1/`. The metadata declares
  `<license type="file">license.txt</license>`. It does NOT
  declare an SPDX expression, so the licence claim rests on the
  shipped `license.txt` file, not on a metadata expression.
- Licence text in `license.txt`: MIT wording, headed
  "Copyright 2020 Mark Heath". The full text is reproduced in
  Appendix B. The condition in that text requires the copyright
  notice and permission notice to be included in all copies or
  substantial portions of the software. This section is that
  inclusion for the NAudio component.
- Sub-packages: NAudio 2.2.1 pulls NAudio.Asio, NAudio.Core,
  NAudio.Midi, NAudio.Wasapi, NAudio.WinForms, and NAudio.WinMM,
  each version 2.2.1. Each sub-package metadata file in the same
  NuGet cache declares `<license type="expression">MIT</license>`.
  The sub-package licence files themselves were not individually
  extracted beyond their metadata declarations. The DLLs shipped
  in `publish-app/` and `publish-staging/` (NAudio.dll,
  NAudio.Core.dll, NAudio.Wasapi.dll, NAudio.WinMM.dll,
  NAudio.Midi.dll, NAudio.Asio.dll, NAudio.WinForms.dll) come
  from these packages.
- Transitive dependencies beyond the NAudio sub-packages were
  not audited in Batch 1. See section 6.

## 3. CommunityToolkit.Mvvm

- Component: CommunityToolkit.Mvvm, version 8.3.2, authors
  Microsoft. Referenced by `Keystream.App` in
  `Keystream.App/Keystream.App.csproj` as
  `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />`.
- Project: https://github.com/CommunityToolkit/dotnet (from the
  package metadata; the metadata records commit
  5320d4f621e145c60ef4180ea66fe57f12f0f58a).
- What was checked: the package metadata file
  `communitytoolkit.mvvm.nuspec` and the file `License.md`
  inside the extracted NuGet package at
  `~/tmpdir/nugetpkgs/communitytoolkit.mvvm/8.3.2/`. The
  metadata declares `<license type="expression">MIT</license>`
  and `<copyright>(c) .NET Foundation and Contributors. All
  rights reserved.</copyright>`. `License.md` is headed
  "Copyright (c) .NET Foundation and Contributors" and contains
  the MIT licence wording, substantively the same grant and
  conditions as Appendix B, with typographic differences only
  (curly quotes and italic emphasis). The MIT condition
  requiring inclusion of the copyright and permission notices
  is met by this section for the CommunityToolkit.Mvvm
  component.
- The package also ships `ThirdPartyNotices.txt`, which lists
  components incorporated into the Toolkit itself. Those
  notices were read only to confirm the file exists; their
  contents are not reproduced here, and no claim is made that
  they are all satisfied by this file alone. The Toolkit
  package is used as a binary dependency; its own third-party
  notices travel with the package as shipped by its authors
  and are not part of the Keystream source tree.

## 4. Fonts

Both font files below are shipped as resources in
`Keystream.App/Assets/` and are referenced in
`Keystream.App/Keystream.App.csproj` as
`<Resource Include="Assets\*.ttf" />`. The licence statements
below are quoted verbatim from each file's name table
(nameID 0, 9, 13, and related records), read with fontTools.
The full SIL Open Font License version 1.1 text is reproduced
in Appendix C. The font files themselves were not compared
against upstream downloads in Batch 1, so no claim is made
here that they are byte-identical to any upstream release,
and no claim is made that they are unmodified.

### 4.1 Orbitron (Orbitron.ttf)

- File: `Keystream.App/Assets/Orbitron.ttf`, version 2.001,
  family Orbitron, style Regular.
- Copyright (nameID 0): `Copyright 2018 The Orbitron Project
  Authors (https://github.com/theleagueof/orbitron), with
  Reserved Font Name: "Orbitron".`
- Designer (nameID 9): Matt McInerney. Manufacturer
  (nameID 8): The League of Moveable Type.
- Licence (nameID 13): `This Font Software is licensed under
  the SIL Open Font License, Version 1.1. This license is
  available with a FAQ at: http://scripts.sil.org/OFL`
- Reserved Font Name: Orbitron. Under OFL condition 3, no
  Modified Version of this Font Software may use the Reserved
  Font Name unless explicit written permission is granted by
  the corresponding Copyright Holder. Keystream ships the
  file as found and does not distribute a modified version
  under the name Orbitron.
- Attribution: Orbitron, Copyright 2018 The Orbitron Project
  Authors, licensed under the SIL Open Font License,
  Version 1.1. See Appendix C.

### 4.2 Rajdhani Bold (Rajdhani-Bold.ttf)

- File: `Keystream.App/Assets/Rajdhani-Bold.ttf`, version
  1.201, family Rajdhani, style Bold.
- Copyright (nameID 0): `Copyright (c) 2014 Indian Type
  Foundry (info@indiantypefoundry.com)`
- Designers (nameID 9): Satya Rajpurohit, Jyotish Sonowal.
  Manufacturer (nameID 8): Indian Type Foundry. Trademark
  note (nameID 7): `Rajdhani is a trademark of Indian Type
  Foundry.`
- Licence (nameID 13): `This Font Software is licensed under
  the SIL Open Font License, Version 1.1. This license is
  available with a FAQ at: http://scripts.sil.org/OFL`
- Reserved Font Name: none declared. The name table copyright
  record for this file contains no Reserved Font Name clause.
  No Reserved Font Name is claimed here for Rajdhani.
- Attribution: Rajdhani Bold, Copyright (c) 2014 Indian Type
  Foundry, licensed under the SIL Open Font License,
  Version 1.1. See Appendix C.

## 5. Logo and app icon: TODO, provenance not verified

- Files: `Keystream.App/Assets/keystream.png` (PNG, 256 by
  256, 100442 bytes) and `Keystream.App/app.ico` (Windows
  icon, 9 sizes, 154934 bytes). Both are referenced in
  `Keystream.App/Keystream.App.csproj` and used in the About
  window and as the application icon.
- TODO: authorship and source of the logo artwork were NOT
  verified in Batch 1. No provenance statement, licence, or
  author name was found in the tree for these files, and none
  is asserted here. The project owner must confirm who made
  the logo and under what terms it may be redistributed
  before this section can be closed. Until then, treat logo
  provenance as unknown.

## 6. Scope limits

- Only the direct package references in the `.csproj` files
  (NAudio 2.2.1 and CommunityToolkit.Mvvm 8.3.2), the
  Interception release asset, the two font files, and the
  logo files were checked in Batch 1.
- Transitive NuGet dependencies, .NET runtime components,
  and Windows system libraries were not audited for licence
  terms in Batch 1. No licence claim is made for them here.
- No other third-party code, artwork, or data file in the
  tree was inventoried in Batch 1. If further third-party
  material is found in the tree, it must be added here with
  its evidence before release.

## Sources checked in Batch 1

- Official Interception release page and asset download,
  29 October 2026 (URLs in section 1).
- Upstream Interception README, licence section (dual
  licence statement quoted in section 1).
- `Interception/licenses/non-commercial-usage/LGPL 3.0.txt`
  inside the downloaded release asset (Appendix A source).
- NuGet cache: `~/tmpdir/nugetpkgs/naudio/2.2.1/naudio.nuspec`,
  `~/tmpdir/nugetpkgs/naudio/2.2.1/license.txt`,
  sub-package `.nuspec` files for NAudio 2.2.1, and
  `~/tmpdir/nugetpkgs/communitytoolkit.mvvm/8.3.2/communitytoolkit.mvvm.nuspec`
  plus `License.md` in the same directory.
- Font name tables read with fontTools from the two `.ttf`
  files in `Keystream.App/Assets/`.
- SIL Open Font License version 1.1 text from the official
  SIL page at https://software.sil.org/oflt/ (interim page
  for http://scripts.sil.org/OFL), read 29 October 2026.
  Appendix C is a transcription of that text; dash
  characters were normalized to hyphens in transcription.
- MIT licence wording from the Open Source Initiative page
  for the MIT License, https://opensource.org/license/mit ,
  cross-checked against the NAudio `license.txt` wording.
  Appendix B reproduces the NAudio `license.txt` file
  verbatim, which uses the same MIT wording.

## Appendix A. GNU Lesser General Public License, version 3

The text below is copied verbatim from
`Interception/licenses/non-commercial-usage/LGPL 3.0.txt` in
the official Interception v1.0.1 release asset. It is included
so that a distribution of Keystream accompanies the Combined
Work with a copy of this licence document, as LGPL section
4(b) requires. See the GAP note in section 1 about the
separate GNU GPL version 3 document.

                   GNU LESSER GENERAL PUBLIC LICENSE
                       Version 3, 29 June 2007

 Copyright (C) 2007 Free Software Foundation, Inc. <http://fsf.org/>
 Everyone is permitted to copy and distribute verbatim copies
 of this license document, but changing it is not allowed.


  This version of the GNU Lesser General Public License incorporates
the terms and conditions of version 3 of the GNU General Public
License, supplemented by the additional permissions listed below.

  0. Additional Definitions.

  As used herein, "this License" refers to version 3 of the GNU Lesser
General Public License, and the "GNU GPL" refers to version 3 of the GNU
General Public License.

  "The Library" refers to a covered work governed by this License,
other than an Application or a Combined Work as defined below.

  An "Application" is any work that makes use of an interface provided
by the Library, but which is not otherwise based on the Library.
Defining a subclass of a class defined by the Library is deemed a mode
of using an interface provided by the Library.

  A "Combined Work" is a work produced by combining or linking an
Application with the Library.  The particular version of the Library
with which the Combined Work was made is also called the "Linked
Version".

  The "Minimal Corresponding Source" for a Combined Work means the
Corresponding Source for the Combined Work, excluding any source code
for portions of the Combined Work that, considered in isolation, are
based on the Application, and not on the Linked Version.

  The "Corresponding Application Code" for a Combined Work means the
object code and/or source code for the Application, including any data
and utility programs needed for reproducing the Combined Work from the
Application, but excluding the System Libraries of the Combined Work.

  1. Exception to Section 3 of the GNU GPL.

  You may convey a covered work under sections 3 and 4 of this License
without being bound by section 3 of the GNU GPL.

  2. Conveying Modified Versions.

  If you modify a copy of the Library, and, in your modifications, a
facility refers to a function or data to be supplied by an Application
that uses the facility (other than as an argument passed when the
facility is invoked), then you may convey a copy of the modified
version:

   a) under this License, provided that you make a good faith effort to
   ensure that, in the event an Application does not supply the
   function or data, the facility still operates, and performs
   whatever part of its purpose remains meaningful, or

   b) under the GNU GPL, with none of the additional permissions of
   this License applicable to that copy.

  3. Object Code Incorporating Material from Library Header Files.

  The object code form of an Application may incorporate material from
a header file that is part of the Library.  You may convey such object
code under terms of your choice, provided that, if the incorporated
material is not limited to numerical parameters, data structure
layouts and accessors, or small macros, inline functions and templates
(ten or fewer lines in length), you do both of the following:

   a) Give prominent notice with each copy of the object code that the
   Library is used in it and that the Library and its use are
   covered by this License.

   b) Accompany the object code with a copy of the GNU GPL and this license
   document.

  4. Combined Works.

  You may convey a Combined Work under terms of your choice that,
taken together, effectively do not restrict modification of the
portions of the Library contained in the Combined Work and reverse
engineering for debugging such modifications, if you also do each of
the following:

   a) Give prominent notice with each copy of the Combined Work that
   the Library is used in it and that the Library and its use are
   covered by this License.

   b) Accompany the Combined Work with a copy of the GNU GPL and this license
   document.

   c) For a Combined Work that displays copyright notices during
   execution, include the copyright notice for the Library among
   these notices, as well as a reference directing the user to the
   copies of the GNU GPL and this license document.

   d) Do one of the following:

       0) Convey the Minimal Corresponding Source under the terms of this
       License, and the Corresponding Application Code in a form
       suitable for, and under terms that permit, the user to
       recombine or relink the Application with a modified version of
       the Linked Version to produce a modified Combined Work, in the
       manner specified by section 6 of the GNU GPL for conveying
       Corresponding Source.

       1) Use a suitable shared library mechanism for linking with the
       Library.  A suitable mechanism is one that (a) uses at run time
       a copy of the Library already present on the user's computer
       system, and (b) will operate properly with a modified version
       of the Library that is interface-compatible with the Linked
       Version.

   e) Provide Installation Information, but only if you would otherwise
   be required to provide such information under section 6 of the
   GNU GPL, and only to the extent that such information is
   necessary to install and execute a modified version of the
   Combined Work produced by recombining or relinking the
   Application with a modified version of the Linked Version. (If
   you use option 4d0, the Installation Information must accompany
   the Minimal Corresponding Source and Corresponding Application
   Code. If you use option 4d1, you must provide the Installation
   Information in the manner specified by section 6 of the GNU GPL
   for conveying Corresponding Source.)

  5. Combined Libraries.

  You may place library facilities that are a work based on the
Library side by side in a single library together with other library
facilities that are not Applications and are not covered by this
License, and convey such a combined library under terms of your
choice, if you do both of the following:

   a) Accompany the combined library with a copy of the same work based
   on the Library, uncombined with any other library facilities,
   conveyed under the terms of this License.

   b) Give prominent notice with the combined library that part of it
   is a work based on the Library, and explaining where to find the
   accompanying uncombined form of the same work.

  6. Revised Versions of the GNU Lesser General Public License.

  The Free Software Foundation may publish revised and/or new versions
of the GNU Lesser General Public License from time to time. Such new
versions will be similar in spirit to the present version, but may
differ in detail to address new problems or concerns.

  Each version is given a distinguishing version number. If the
Library as you received it specifies that a certain numbered version
of the GNU Lesser General Public License "or any later version"
applies to it, you have the option of following the terms and
conditions either of that published version or of any later version
published by the Free Software Foundation. If the Library as you
received it does not specify a version number of the GNU Lesser
General Public License, you may choose any version of the GNU Lesser
General Public License ever published by the Free Software Foundation.

  If the Library as you received it specifies that a proxy can decide
whether future versions of the GNU Lesser General Public License shall
apply, that proxy's public statement of acceptance of any version is
permanent authorization for you to choose that version for the
Library.

## Appendix B. MIT licence text (NAudio license.txt, verbatim)

The text below is copied verbatim from `license.txt` in the NAudio 2.2.1 NuGet package. It is the MIT licence wording that governs the NAudio component. The CommunityToolkit.Mvvm `License.md` wording is substantively the same MIT wording with different copyright holder and typography; see section 3.

Copyright 2020 Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.


## Appendix C. SIL Open Font License, version 1.1

The text below is transcribed from the official SIL page listed in the Sources section. The two font copyright statements that precede the licence text in a normal OFL.txt file are given per font in section 4 instead, because the two fonts have different holders. Dash characters were normalized to hyphens in transcription; wording is otherwise as published.

SIL OPEN FONT LICENSE

Version 1.1 - 26 February 2007

PREAMBLE

The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS

"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting - in part or in whole - any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS

Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION

This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER

THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.

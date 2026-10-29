# OpenPoll GitHub release: legal source findings

Research date: 2026-10-29. Facts and quotations only, not legal advice.
OBSERVED = the primary source was fetched and the quote was read in it. UNVERIFIED = not confirmed against a primary source in this pass.

## 1. Indonesia: UU No. 28 Tahun 2014 tentang Hak Cipta

Source [1]: official gazette text, Lembaran Negara RI 2014 No. 266, https://peraturan.go.id/files/uu28-2014bt.pdf (the PDF footer reads www.djpp.kemenkumham.go.id).

**Pasal 36 (works made in an employment relationship / on commission).** OBSERVED [1, page 14]:
> "Kecuali diperjanjikan lain, Pencipta dan Pemegang Hak Cipta atas Ciptaan yang dibuat dalam hubungan kerja atau berdasarkan pesanan yaitu pihak yang membuat Ciptaan."

Related, OBSERVED [1, pages 14-15]:
- Pasal 35(1), government service only: "Kecuali diperjanjikan lain Pemegang Hak Cipta atas Ciptaan yang dibuat oleh Pencipta dalam hubungan dinas, yang dianggap sebagai Pencipta yaitu instansi pemerintah."
- Pasal 37: "Kecuali terbukti sebaliknya, dalam hal badan hukum melakukan Pengumuman, Pendistribusian, atau Komunikasi atas Ciptaan yang berasal dari badan hukum tersebut, dengan tanpa menyebut seseorang sebagai Pencipta, yang dianggap sebagai Pencipta yaitu badan hukum."
- Pasal 16(2)(e): copyright "dapat beralih atau dialihkan, baik seluruh maupun sebagian karena: ... e. perjanjian tertulis".

Fact relevant to the release: under Pasal 36 the default holder is the creator ("pihak yang membuat Ciptaan"), unless the employment contract says otherwise ("Kecuali diperjanjikan lain"). The contents of the PT Arkamaya Guna Saharsa employment agreement were not checked.

**Ideas, methods and functional elements are not protected.** Pasal 41, OBSERVED [1, page 17]:
> "Hasil karya yang tidak dilindungi Hak Cipta meliputi: a. hasil karya yang belum diwujudkan dalam bentuk nyata; b. setiap ide, prosedur, sistem, metode, konsep, prinsip, temuan atau data walaupun telah diungkapkan, dinyatakan, digambarkan, dijelaskan, atau digabungkan dalam sebuah Ciptaan; dan c. alat, benda, atau produk yang diciptakan hanya untuk menyelesaikan masalah teknis atau yang bentuknya hanya ditujukan untuk kebutuhan fungsional."

Definition, Pasal 1 angka 9, OBSERVED [1, page 4]: "Program Komputer adalah seperangkat instruksi yang diekspresikan dalam bentuk bahasa, kode, skema, atau dalam bentuk apapun ..."

**Program Komputer exception.** Pasal 45, OBSERVED [1, page 19]:
> "(1) Penggandaan sebanyak 1 (satu) salinan atau adaptasi Program Komputer yang dilakukan oleh pengguna yang sah dapat dilakukan tanpa izin Pencipta atau Pemegang Hak Cipta jika salinan tersebut digunakan untuk: a. penelitian dan pengembangan Program Komputer tersebut; dan b. arsip atau cadangan atas Program Komputer yang diperoleh secara sah untuk mencegah kehilangan, kerusakan, atau tidak dapat dioperasikan.
> (2) Apabila penggunaan Program Komputer telah berakhir, salinan atau adaptasi Program Komputer tersebut harus dimusnahkan."

Pasal 46(2)(d), OBSERVED [1, page 19]: personal-use copying does not cover "Program Komputer, kecuali sebagaimana dimaksud dalam Pasal 45 ayat (1)".

**Reverse engineering / interoperability.** No article in the main text names reverse engineering ("rekayasa balik"), decompilation, or interoperability. OBSERVED as absent in the main text [1], which was read in full for Bab V–VI (Pasal 40–51). The official *Penjelasan* (TLN No. 5599) was not readable here: the BPK and archive.org PDFs are image scans with no extractable text. Whether the Penjelasan of Pasal 45 adds detail is UNVERIFIED.

**Pembatasan (Pasal 43, 44).** OBSERVED [1, pages 17-18]:
- Pasal 43(d): "pembuatan dan penyebarluasan konten Hak Cipta melalui media teknologi informasi dan komunikasi yang bersifat tidak komersial dan/atau menguntungkan Pencipta atau pihak terkait, atau Pencipta tersebut menyatakan tidak keberatan atas pembuatan dan penyebarluasan tersebut."
- Pasal 44(1): "Penggunaan, pengambilan, Penggandaan, dan/atau pengubahan suatu Ciptaan dan/atau produk Hak Terkait secara seluruh atau sebagian yang substansial tidak dianggap sebagai pelanggaran Hak Cipta jika sumbernya disebutkan atau dicantumkan secara lengkap untuk keperluan: a. pendidikan, penelitian, penulisan karya ilmiah, penyusunan laporan, penulisan kritik atau tinjauan suatu masalah dengan tidak merugikan kepentingan yang wajar dari Pencipta atau Pemegang Hak Cipta; ..."

**General rule on copying and distribution.** Pasal 9(3), OBSERVED [1, page 7]: "Setiap Orang yang tanpa izin Pencipta atau Pemegang Hak Cipta dilarang melakukan Penggandaan dan/atau Penggunaan Secara Komersial Ciptaan." This bears on redistributing mbpoll.exe, its CHM, or AOSen.exe (see §7).

**Technical protection measures.** Pasal 52, OBSERVED [1, pages 21-22]: "Setiap Orang dilarang merusak, memusnahkan, menghilangkan, atau membuat tidak berfungsi sarana kontrol teknologi yang digunakan sebagai pelindung Ciptaan ..." The local `re/REPORT.md` states that the registration-key logic was "deliberately **not analyzed further**".

Not checked: later amendments. Perppu 2/2022 and UU 6/2023 (Cipta Kerja) were not checked for changes to these articles. The Mahkamah Konstitusi annotation PDF (decision 84/PUU-XXI/2023) was downloaded but is image-only, so it could not be read. UNVERIFIED.

## 2. EU: CJEU C-406/10 SAS Institute v World Programming (Grand Chamber, 2 May 2012)

Source [2]: EUR-Lex, ECLI:EU:C:2012:259, https://eur-lex.europa.eu/legal-content/EN/TXT/PDF/?uri=CELEX%3A62010CJ0406

Operative part ("On those grounds, the Court (Grand Chamber) hereby rules"), OBSERVED [2]:
> "1. Article 1(2) of Council Directive 91/250/EEC ... must be interpreted as meaning that neither the functionality of a computer program nor the programming language and the format of data files used in a computer program in order to exploit certain of its functions constitute a form of expression of that program and, as such, are not protected by copyright in computer programs for the purposes of that directive."

> "2. Article 5(3) of Directive 91/250 must be interpreted as meaning that a person who has obtained a copy of a computer program under a licence is entitled, without the authorisation of the owner of the copyright, to observe, study or test the functioning of that program so as to determine the ideas and principles which underlie any element of the program, in the case where that person carries out acts covered by that licence and acts of loading and running necessary for the use of the computer program, and on condition that that person does not infringe the exclusive rights of the owner of the copyright in that program."

> "3. Article 2(a) of Directive 2001/29/EC ... must be interpreted as meaning that the reproduction, in a computer program or a user manual for that program, of certain elements described in the user manual for another computer program protected by copyright is capable of constituting an infringement of the copyright in the latter manual if — this being a matter for the national court to ascertain — that reproduction constitutes the expression of the intellectual creation of the author of the user manual for the computer program protected by copyright."

Supporting paragraphs, OBSERVED [2]:
- Para 61: "the copyright in a computer program cannot be infringed where, as in the present case, the lawful acquirer of the licence did not have access to the source code of the computer program to which that licence relates, but merely studied, observed and tested that program in order to reproduce its functionality in a second program."
- Para 59: the copyright owner "may not prevent, by relying on the licensing agreement, the person who has obtained that licence from determining the ideas and principles which underlie all the elements of that program in the case where that person carries out acts which that licence permits him to perform ..."
- Para 66: "the keywords, syntax, commands and combinations of commands, options, defaults and iterations consist of words, figures or mathematical concepts which, considered in isolation, are not, as such, an intellectual creation of the author of the computer program."
- Para 67: "It is only through the choice, sequence and combination of those words, figures or mathematical concepts that the author may express his creativity in an original manner ..."

Fact relevant to the release: holding 3 and paras 66–67 concern copying *manual text*. They apply to how the CHM help text is reused, separately from the menu and dialog layout. Directive 91/250 has since been codified as Directive 2009/24/EC, which was not checked here.

## 3. US: Lotus Development Corp. v. Borland International, 49 F.3d 807 (1st Cir. 1995)

Source [3]: opinion text as hosted by MIT, https://groups.csail.mit.edu/mac/classes/6.805/articles/int-prop/lotus/lotus-borland-appeal-Mar95.html (Justia returned HTTP 403). Other copies: https://cyber.harvard.edu/people/tfisher/IP/1995_Lotus.pdf and https://www.law.berkeley.edu/archive/files/Lotus_v_Borland.pdf (found in search, not opened).

OBSERVED [3]:
> "We hold that the Lotus menu command hierarchy is an uncopyrightable 'method of operation.' The Lotus menu command hierarchy provides the means by which users control and operate Lotus 1-2-3."

> "We think that 'method of operation,' as that term is used in § 102(b), refers to the means by which a person operates something, whether it be a car, a food processor, or a computer."

Conclusion section, OBSERVED [3]:
> "Because we hold that the Lotus menu command hierarchy is uncopyrightable subject matter, we further hold that Borland did not infringe Lotus's copyright by copying it. ... Reversed."

UNVERIFIED: the US Supreme Court affirmed by an equally divided court (516 U.S. 233, 1996), so the case set no Supreme Court precedent. The primary page was not retrieved.

## 4. MODBUS trademark (Schneider Electric / Modbus Organization)

Source [4]: Modbus Organization, *MODBUS/TCP Security Protocol Specification* MB-TCP-Security-v36 (2021-07-30), https://www.modbus.org/file/secure/modbussecurityprotocol.pdf. OBSERVED [4, page 1]:
> "MODBUS® is a registered trademark of Schneider Electric USA, Inc., used under license by Modbus Organization, Inc."

Source [5]: https://www.modbus.org/legal, "TRADEMARKS" section. OBSERVED [5]:
> "Modicon, Merlin Gerin, Square D, Telemecanique, associated logos and the names of affiliated, associated and/or related companies, as well as their products referenced in this website are trademarks or registered trademarks of Schneider Electric and/or such related companies. Other products and company names referenced on this website may be the trademarks of their respective owners. No permission is granted for using the company/product names, trademarks or trade names included on this website for any reason other than for non-commercial, informational purposes."

Source [6]: specification download EULA, https://modbus.org/specifications. OBSERVED [6]:
> "You may reproduce and distribute an unlimited number of copies of the SOFTWARE PRODUCT; provided that each copy shall be a true and complete copy, including all copyright and trademark notices, and shall be accompanied by a copy of this EULA."

Third-party naming guidance: no modbus.org page giving rules for third-party product names containing "Modbus" was found on /legal, /faq, /specifications or /modbus-specifications. Search did surface a "Conformance Testing" page and a "Toolkit License Agreement" (https://www.modbus.org/toolkit-license-agreement), but neither was opened. Status of naming guidance: UNVERIFIED / not found.

Local artifact, OBSERVED: `artifacts/quick.html` line 262 contains an older notice: "Copyright © 2009 ModbusTools - All rights reserved. Modbus is trademark of Modicon."

## 5. Witte Software / modbustools.com: "Modbus Poll" trademark and EULA

Source [7]: Modbus Poll User Manual §1.2 "End-User License Agreement", https://www.modbustools.com/mbpoll-user-manual.html. OBSERVED [7]:
> "Copyright. Modbus Poll ("The Software") is copyright 2002-2026 by Witte Software, all rights reserved."

> "Distribution. Provided that you do not include your License Key you are hereby licensed to make copies of the Software; give exact copies of the original to anyone; and distribute the Software in its unmodified form via electronic means. You are specifically prohibited from charging for any such copies."

> "This is not free software. You are hereby licensed to use the Software for evaluation purposes without charge for a period of 30 days."

**Reverse engineering clause.** The current online EULA in [7] contains no reverse-engineering, decompilation or disassembly clause. OBSERVED as absent: the full EULA section was extracted and searched for "reverse", "decompil" and "disassembl". The EULA embedded in the locally held mbpoll.exe/CHM version was not compared (no EULA page matched in the local tree). UNVERIFIED for that version.

**Trademark presentation.** OBSERVED [8] on https://www.modbustools.com/ and https://www.modbustools.com/modbus_poll.html: the footer reads only "Copyright © … Witte Software - All rights reserved." These pages show no ®/™ mark or trademark statement for "Modbus Poll". No USPTO or EUIPO search was run. Whether "Modbus Poll" is a registered mark is UNVERIFIED.

UNVERIFIED (search snippet only): a different vendor offers a product also called "Modbus Poll" at https://www.modbuspoll.com/ (Mac App Store link).

## 6. Name collision: "OpenPoll"

Source [9]: GitHub repository search API, https://api.github.com/search/repositories?q=openpoll+in:name, queried 2026-10-29. OBSERVED: total_count 45. Most results are polling/survey apps, for example julianfbeck/openpoll.app ("OpenPoll is a free and open-source platform to create and share polls.", site https://openpoll.app/), KazuAlex/openpollslack, PollingInc/openpoll and s-alad/openpoll.

**Direct collision in the same domain.** Source [10]: https://github.com/JoshuaVlantis/OpenPoll-Modbus, OBSERVED via the GitHub API and the README:
- License GPL-3.0, language C#, created 2022-01-10, last push 2026-05-20, 13 stars.
- README: "# OpenPoll · OpenSlave — Free, open-source Modbus tooling for engineers — runs natively on Linux, Windows, and macOS via .NET 8 + Avalonia 11."
- README: "**OpenPoll** — Modbus master / client. Reads and writes registers from any slave."
- README: "Feature parity with Modbus Poll / Modbus Slave from modbustools.com."

PyPI: https://pypi.org/pypi/openpoll/json returned HTTP 404, so no PyPI package named `openpoll` was found. OBSERVED. No trademark-registry search for "OpenPoll" was run (UNVERIFIED).

## 7. GitHub policies on third-party copyrighted binaries

Source [11]: GitHub Acceptable Use Policies §3, https://docs.github.com/en/site-policy/acceptable-use-policies/github-acceptable-use-policies. OBSERVED [11]:
> "We do not allow content or activity on GitHub that: infringes any proprietary right of any party, including patent, trademark, trade secret, copyright, right of publicity, or other right; unlawfully shares unauthorized product licensing keys, software for generating unauthorized product licensing keys, or software for bypassing checks for product licensing keys, including extension of a free license beyond its trial period; ..."

Source [12]: GitHub Terms of Service §D, https://docs.github.com/en/site-policy/github-terms/github-terms-of-service. OBSERVED [12]:
> "You are responsible for Your Content and any harm resulting from it. This includes Content you post, upload, or make available through the Service."
> "We may refuse or remove User-Generated Content that violates applicable law or our terms and policies."
> "If you post Content you did not create or own, you are responsible for ensuring it is licensed under terms that permit these uses."
> "Whenever you add Content to a repository containing notice of a license, you license that Content under the same terms, and you agree that you have the right to license that ..."

Source [13]: GitHub DMCA Takedown Policy, https://docs.github.com/en/site-policy/content-removal-policies/dmca-takedown-policy. OBSERVED [13]:
> "The DMCA also prohibits the circumvention of technical measures that effectively control access to works protected by copyright."
> "It is the policy of GitHub, in appropriate circumstances and in its sole discretion, to disable and terminate the accounts of users who may infringe upon the copyrights or other intellectual property rights of GitHub or others."
> On forks: "If at the time that you submitted your notice, you identified all existing forks of that repository as allegedly infringing, we would process a valid claim against all forks in that network at the time we process the notice."

Facts relevant to the release:
- The working tree holds mbpoll.exe, its CHM, decompiled CHM pages, resource dumps and AOSen.exe.
- The Modbus Poll EULA [7] permits distributing "exact copies of the original" in "unmodified form". It says nothing about decompiled CHM HTML or resource dumps.
- No license terms for AOSen.exe (Volison) were checked (UNVERIFIED).
- Git history keeps files that were committed and later deleted.

## Sources

| # | Source | URL or artifact | Key claim | Type | Status |
|---|---|---|---|---|---|
| 1 | UU 28/2014, LN 2014 No. 266 | https://peraturan.go.id/files/uu28-2014bt.pdf | Pasal 36, 41, 43–46, 52 text | Statute (official) | OBSERVED |
| 2 | CJEU C-406/10 judgment | https://eur-lex.europa.eu/legal-content/EN/TXT/PDF/?uri=CELEX%3A62010CJ0406 | Functionality, language and data formats not protected; observe/study/test right | Court judgment | OBSERVED |
| 3 | Lotus v. Borland, 49 F.3d 807 | https://groups.csail.mit.edu/mac/classes/6.805/articles/int-prop/lotus/lotus-borland-appeal-Mar95.html | Menu command hierarchy is an uncopyrightable method of operation | Court opinion (mirror) | OBSERVED |
| 4 | MODBUS/TCP Security spec v36 | https://www.modbus.org/file/secure/modbussecurityprotocol.pdf | MODBUS® registered TM of Schneider Electric USA | Official spec | OBSERVED |
| 5 | modbus.org Legal | https://www.modbus.org/legal | Trademark notice; names only for non-commercial, informational purposes | Official page | OBSERVED |
| 6 | modbus.org spec EULA | https://modbus.org/specifications | Copies must keep copyright/trademark notices | Official page | OBSERVED |
| 7 | Modbus Poll User Manual EULA | https://www.modbustools.com/mbpoll-user-manual.html | Copyright, distribution, evaluation; no RE clause | Vendor page | OBSERVED |
| 8 | modbustools.com home/product | https://www.modbustools.com/modbus_poll.html | Footer copyright only, no TM marking | Vendor page | OBSERVED |
| 9 | GitHub search API | https://api.github.com/search/repositories?q=openpoll+in:name | 45 repos named openpoll* | Platform data | OBSERVED |
| 10 | JoshuaVlantis/OpenPoll-Modbus | https://github.com/JoshuaVlantis/OpenPoll-Modbus | GPL-3.0 C# Modbus master named OpenPoll | Repository | OBSERVED |
| 11 | GitHub Acceptable Use Policies | https://docs.github.com/en/site-policy/acceptable-use-policies/github-acceptable-use-policies | No infringing content | Platform policy | OBSERVED |
| 12 | GitHub Terms of Service | https://docs.github.com/en/site-policy/github-terms/github-terms-of-service | User responsible; content not owned must be licensed | Platform policy | OBSERVED |
| 13 | GitHub DMCA Takedown Policy | https://docs.github.com/en/site-policy/content-removal-policies/dmca-takedown-policy | Takedown, repeat infringer, forks, circumvention | Platform policy | OBSERVED |

## Coverage status

- Q1: Main text OBSERVED. Penjelasan and post-2014 amendments UNVERIFIED (image-only PDFs).
- Q2: OBSERVED.
- Q3: OBSERVED. The Supreme Court 4–4 affirmance is UNVERIFIED.
- Q4: Trademark notice OBSERVED. No third-party naming guidance found (UNVERIFIED).
- Q5: EULA OBSERVED; it has no RE clause. Registration of "Modbus Poll" UNVERIFIED (no registry search).
- Q6: OBSERVED. There is a same-domain collision: JoshuaVlantis/OpenPoll-Modbus.
- Q7: OBSERVED.
